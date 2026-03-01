const BYTECODE_MAGIC = "CODE";
const BYTECODE_VERSION = 5;
const HEADER_SIZE = 13;
const DEBUG_ENTRY_SIZE = 12;

const OptionalNone = Symbol("optional-none");

const OpCode = {
  PushConst: 0x01,
  Add: 0x02,
  Sub: 0x03,
  Mul: 0x04,
  Div: 0x05,
  Print: 0x06,
  Dup: 0x07,
  Swap: 0x08,
  Pop: 0x09,
  Jump: 0x0a,
  JumpIfZero: 0x0b,
  JumpIfNotZero: 0x0c,
  Load: 0x0d,
  Store: 0x0e,
  Eq: 0x0f,
  Lt: 0x10,
  Gt: 0x11,
  Call: 0x12,
  Ret: 0x13,
  PushString: 0x14,
  ThrowError: 0x15,
  NewArray: 0x16,
  ArrayLength: 0x17,
  ArrayGet: 0x18,
  NewArrayN: 0x19,
  OptionalNone: 0x1a,
  OptionalHas: 0x1b,
  OptionalValue: 0x1c,
  OptionalOr: 0x1d,
  ArraySet: 0x1e,
  NewObject: 0x1f,
  GetField: 0x20,
  SetField: 0x21,
  GetTypeName: 0x22,
  InterfaceCall: 0x23,
  Mod: 0x24,
  TimeUnixMs: 0x25,
  TimeUnixUs: 0x26,
  TimeMonoNs: 0x27,
  TimeMonoTicks: 0x28,
  TimeMonoTicksPerSecond: 0x29,
  HostCall: 0x2a,
  Halt: 0xff
};

const Utf8Decoder = new TextDecoder("utf-8");

function decodeBase64Bytes(base64Text) {
  const binary = atob(base64Text);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

export function extractBytecodePayload(bytes, fileName) {
  const lower = (fileName || "").toLowerCase();
  if (!lower.endsWith(".codelib")) {
    return bytes;
  }

  const jsonText = Utf8Decoder.decode(bytes);
  let payload;
  try {
    payload = JSON.parse(jsonText);
  } catch (error) {
    throw new Error(`Invalid .codelib JSON: ${error.message}`);
  }

  if (!payload || typeof payload.bytecode !== "string") {
    throw new Error("Invalid .codelib: missing 'bytecode' field.");
  }

  return decodeBase64Bytes(payload.bytecode);
}

function readHeader(bytes) {
  if (bytes.length < HEADER_SIZE) {
    throw new Error("Bytecode too short for header.");
  }

  const magic = String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]);
  if (magic !== BYTECODE_MAGIC) {
    throw new Error(`Invalid bytecode magic '${magic}'.`);
  }

  const version = bytes[4];
  if (version !== BYTECODE_VERSION) {
    throw new Error(`Unsupported bytecode version ${version}, expected ${BYTECODE_VERSION}.`);
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const codeSize = view.getInt32(5, true);
  const debugCount = view.getInt32(9, true);
  const codeEnd = HEADER_SIZE + codeSize;
  const totalSize = codeEnd + debugCount * DEBUG_ENTRY_SIZE;
  if (totalSize > bytes.length) {
    throw new Error("Bytecode truncated: header sizes exceed file length.");
  }

  const debugMap = new Map();
  let debugOffset = codeEnd;
  for (let i = 0; i < debugCount; i += 1) {
    const ip = view.getInt32(debugOffset, true);
    const line = view.getInt32(debugOffset + 4, true);
    const column = view.getInt32(debugOffset + 8, true);
    debugMap.set(ip, { line, column });
    debugOffset += DEBUG_ENTRY_SIZE;
  }

  return { codeEnd, debugMap, view };
}

export class VmRuntimeError extends Error {
  constructor(message, payload) {
    super(message);
    this.name = "VmRuntimeError";
    this.payload = payload;
  }
}

function isNumberValue(value) {
  return typeof value === "number";
}

function toNumber(value, fail) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  fail(`Expected number on stack, found ${typeof value}`);
  return 0;
}

function formatVmError(errorObject) {
  const line = Number.isInteger(errorObject.line) ? errorObject.line : -1;
  const column = Number.isInteger(errorObject.column) ? errorObject.column : -1;
  return `${errorObject.type}: ${errorObject.message} at ${line}:${column}`;
}

function isVmError(value) {
  return value && typeof value === "object" && value.__vmError === true;
}

export class WebVm {
  constructor(bytecodeBytes, options = {}) {
    this.bytes = bytecodeBytes;
    const header = readHeader(this.bytes);
    this.codeEnd = header.codeEnd;
    this.debugMap = header.debugMap;
    this.view = header.view;
    this.ip = HEADER_SIZE;

    this.stack = [];
    this.locals = new Array(Math.max(8, options.initialLocals || 8)).fill(0);
    this.callStack = [];
    this.interfaceDispatchCache = new Map();
    this.nextWindowHandle = 1;

    this.output = typeof options.output === "function" ? options.output : line => console.log(line);
    this.hostTarget = "vm-web";
    this.monoOriginMs = performance.now();
    this.hostBindings = new Map();

    this.initializeHostBindings();
  }

  initializeHostBindings() {
    this.hostBindings.set("std.io.print", {
      arity: 1,
      handler: args => {
        const value = args[0];
        if (isVmError(value)) {
          this.output(formatVmError(value));
        } else {
          this.output(String(value));
        }
        return 0;
      }
    });

    this.hostBindings.set("std.time.unix_ms", {
      arity: 0,
      handler: () => Math.trunc(Date.now())
    });

    this.hostBindings.set("std.time.unix_us", {
      arity: 0,
      handler: () => Math.trunc(Date.now() * 1000)
    });

    this.hostBindings.set("std.time.mono_ns", {
      arity: 0,
      handler: () => Math.trunc((performance.now() - this.monoOriginMs) * 1_000_000)
    });

    // Web runtime exposes monotonic microsecond ticks.
    this.hostBindings.set("std.time.mono_ticks", {
      arity: 0,
      handler: () => Math.trunc((performance.now() - this.monoOriginMs) * 1000)
    });

    this.hostBindings.set("std.time.mono_ticks_per_second", {
      arity: 0,
      handler: () => 1_000_000
    });

    this.registerUnsupportedBinding("std.io.read_line", 0, "native-only API");
    this.registerUnsupportedBinding("std.time.sleep_ms", 1, "native-only API");

    this.hostBindings.set("engine.window.create", {
      arity: 3,
      handler: () => this.nextWindowHandle++
    });
    this.hostBindings.set("engine.window.should_close", {
      arity: 1,
      handler: () => 1
    });
    this.hostBindings.set("engine.window.present", {
      arity: 1,
      handler: () => 0
    });
    this.hostBindings.set("engine.input.key_down", {
      arity: 2,
      handler: () => 0
    });
    this.hostBindings.set("engine.gfx.clear", {
      arity: 5,
      handler: () => 0
    });
    this.hostBindings.set("engine.gfx.draw_rect", {
      arity: 9,
      handler: () => 0
    });
  }

  registerUnsupportedBinding(symbol, arity, reason) {
    this.hostBindings.set(symbol, {
      arity,
      handler: () => {
        this.throwRuntime(
          `Host binding '${symbol}' is not available on target '${this.hostTarget}': ${reason}.`,
          "HostBindingError"
        );
        return 0;
      }
    });
  }

  run() {
    while (true) {
      if (this.ip >= this.codeEnd) {
        this.throwRuntime("Execution fell off the end of the program.");
      }

      const op = this.bytes[this.ip];
      this.ip += 1;

      switch (op) {
        case OpCode.PushConst:
          this.stack.push(this.readIntOperand());
          break;

        case OpCode.PushString: {
          const length = this.readIntOperand();
          this.ensureBytes(length);
          const text = Utf8Decoder.decode(this.bytes.subarray(this.ip, this.ip + length));
          this.ip += length;
          this.stack.push(text);
          break;
        }

        case OpCode.Add: {
          const [left, right] = this.popAny2();
          if (typeof left === "string" || typeof right === "string") {
            this.stack.push(String(left) + String(right));
          } else {
            this.stack.push(toNumber(left, m => this.throwRuntime(m)) + toNumber(right, m => this.throwRuntime(m)));
          }
          break;
        }

        case OpCode.Sub:
          this.numericBinary((a, b) => a - b);
          break;

        case OpCode.Mul:
          this.numericBinary((a, b) => a * b);
          break;

        case OpCode.Div:
          this.numericBinary((a, b) => {
            if (b === 0) {
              this.throwRuntime("Division by zero in bytecode.");
            }
            return a / b;
          });
          break;

        case OpCode.Mod:
          this.numericBinary((a, b) => {
            if (b === 0) {
              this.throwRuntime("Modulo by zero in bytecode.");
            }
            return a % b;
          });
          break;

        case OpCode.Print: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (isVmError(value)) {
            this.output(formatVmError(value));
          } else {
            this.output(String(value));
          }
          break;
        }

        case OpCode.Dup:
          this.ensureStack(1);
          this.stack.push(this.stack[this.stack.length - 1]);
          break;

        case OpCode.Swap: {
          this.ensureStack(2);
          const a = this.stack.pop();
          const b = this.stack.pop();
          this.stack.push(a);
          this.stack.push(b);
          break;
        }

        case OpCode.Pop:
          this.ensureStack(1);
          this.stack.pop();
          break;

        case OpCode.Jump:
          this.ip = this.readIntOperand();
          break;

        case OpCode.JumpIfZero: {
          const test = this.popNumber();
          const target = this.readIntOperand();
          if (test === 0) {
            this.ip = target;
          }
          break;
        }

        case OpCode.JumpIfNotZero: {
          const test = this.popNumber();
          const target = this.readIntOperand();
          if (test !== 0) {
            this.ip = target;
          }
          break;
        }

        case OpCode.Load: {
          const slot = this.readIntOperand();
          this.ensureLocals(slot);
          this.stack.push(this.locals[slot]);
          break;
        }

        case OpCode.Store: {
          const slot = this.readIntOperand();
          this.ensureStack(1);
          this.ensureLocals(slot);
          this.locals[slot] = this.stack.pop();
          break;
        }

        case OpCode.Eq: {
          const [left, right] = this.popAny2();
          if (isNumberValue(left) && isNumberValue(right)) {
            this.stack.push(left === right ? 1 : 0);
          } else {
            this.stack.push(left === right ? 1 : 0);
          }
          break;
        }

        case OpCode.Lt:
          this.numericBinary((a, b) => (a < b ? 1 : 0));
          break;

        case OpCode.Gt:
          this.numericBinary((a, b) => (a > b ? 1 : 0));
          break;

        case OpCode.Call: {
          const callIp = this.ip - 1;
          const target = this.readIntOperand();
          const argCount = this.readIntOperand();
          const localCount = this.readIntOperand();
          const newLocals = new Array(Math.max(localCount, argCount)).fill(0);
          for (let i = argCount - 1; i >= 0; i -= 1) {
            this.ensureStack(1);
            newLocals[i] = this.stack.pop();
          }
          this.callStack.push({ returnIp: this.ip, callIp, locals: this.locals });
          this.locals = newLocals;
          this.ip = target;
          break;
        }

        case OpCode.Ret: {
          this.ensureStack(1);
          const retVal = this.stack.pop();
          if (this.callStack.length === 0) {
            return;
          }
          const frame = this.callStack.pop();
          this.locals = frame.locals;
          this.ip = frame.returnIp;
          this.stack.push(retVal);
          break;
        }

        case OpCode.NewArray: {
          const count = this.readIntOperand();
          this.ensureStack(count);
          const items = new Array(count);
          for (let i = count - 1; i >= 0; i -= 1) {
            items[i] = this.stack.pop();
          }
          this.stack.push(items);
          break;
        }

        case OpCode.ArrayLength: {
          this.ensureStack(1);
          const arr = this.stack.pop();
          if (!Array.isArray(arr)) {
            this.throwRuntime("ArrayLength expects array");
          }
          this.stack.push(arr.length);
          break;
        }

        case OpCode.ArrayGet: {
          this.ensureStack(2);
          const index = Math.trunc(this.popNumber());
          const arr = this.stack.pop();
          if (!Array.isArray(arr)) {
            this.throwRuntime("ArrayGet expects array");
          }
          if (index < 0 || index >= arr.length) {
            this.throwRuntime("Array index out of range");
          }
          this.stack.push(arr[index]);
          break;
        }

        case OpCode.ArraySet: {
          this.ensureStack(3);
          const value = this.stack.pop();
          const index = Math.trunc(this.popNumber());
          const arr = this.stack.pop();
          if (!Array.isArray(arr)) {
            this.throwRuntime("ArraySet expects array");
          }
          if (index < 0 || index >= arr.length) {
            this.throwRuntime("Array index out of range");
          }
          arr[index] = value;
          this.stack.push(value);
          break;
        }

        case OpCode.NewArrayN: {
          this.ensureStack(1);
          const size = Math.trunc(this.popNumber());
          if (size < 0) {
            this.throwRuntime("Array size must be non-negative");
          }
          this.stack.push(new Array(size).fill(0));
          break;
        }

        case OpCode.OptionalNone:
          this.stack.push(OptionalNone);
          break;

        case OpCode.OptionalHas: {
          this.ensureStack(1);
          const value = this.stack.pop();
          this.stack.push(value === OptionalNone ? 0 : 1);
          break;
        }

        case OpCode.OptionalValue: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (value === OptionalNone) {
            this.throwRuntime("Optional has no value");
          }
          this.stack.push(value);
          break;
        }

        case OpCode.OptionalOr: {
          this.ensureStack(2);
          const fallback = this.stack.pop();
          const value = this.stack.pop();
          this.stack.push(value === OptionalNone ? fallback : value);
          break;
        }

        case OpCode.NewObject: {
          const typeName = this.readStringOperand();
          this.stack.push({ __vmObject: true, typeName, fields: new Map() });
          break;
        }

        case OpCode.GetField: {
          const fieldName = this.readStringOperand();
          this.ensureStack(1);
          const target = this.stack.pop();
          if (!target || target.__vmObject !== true) {
            this.throwRuntime("GetField expects object");
          }
          if (!target.fields.has(fieldName)) {
            this.throwRuntime(`Field '${fieldName}' is not initialized on object '${target.typeName}'`);
          }
          this.stack.push(target.fields.get(fieldName));
          break;
        }

        case OpCode.SetField: {
          const fieldName = this.readStringOperand();
          this.ensureStack(2);
          const value = this.stack.pop();
          const target = this.stack.pop();
          if (!target || target.__vmObject !== true) {
            this.throwRuntime("SetField expects object");
          }
          target.fields.set(fieldName, value);
          this.stack.push(value);
          break;
        }

        case OpCode.GetTypeName: {
          this.ensureStack(1);
          const target = this.stack.pop();
          if (!target || target.__vmObject !== true) {
            this.throwRuntime("GetTypeName expects object");
          }
          this.stack.push(target.typeName);
          break;
        }

        case OpCode.InterfaceCall: {
          const callIp = this.ip - 1;
          let dispatch = this.interfaceDispatchCache.get(callIp);
          if (!dispatch) {
            dispatch = this.readInterfaceDispatchTable();
            this.interfaceDispatchCache.set(callIp, dispatch);
          } else {
            this.ip = dispatch.nextIp;
          }

          this.ensureStack(dispatch.explicitArgCount + 1);
          const args = new Array(dispatch.explicitArgCount);
          for (let i = dispatch.explicitArgCount - 1; i >= 0; i -= 1) {
            args[i] = this.stack.pop();
          }

          const target = this.stack.pop();
          if (!target || target.__vmObject !== true) {
            this.throwRuntime("InterfaceCall expects object target");
          }

          const entry = dispatch.entries.get(target.typeName);
          if (!entry) {
            this.throwRuntime(`No implementation for interface call on runtime object '${target.typeName}'`);
          }

          const totalArgCount = dispatch.explicitArgCount + 1;
          const newLocals = new Array(Math.max(entry.localCount, totalArgCount)).fill(0);
          newLocals[0] = target;
          for (let i = 0; i < args.length; i += 1) {
            newLocals[i + 1] = args[i];
          }

          this.callStack.push({ returnIp: this.ip, callIp, locals: this.locals });
          this.locals = newLocals;
          this.ip = entry.targetIp;
          break;
        }

        case OpCode.ThrowError: {
          this.ensureStack(1);
          const message = String(this.stack.pop() ?? "error");
          this.throwRuntime(message, "UserError");
          break;
        }

        case OpCode.TimeUnixMs:
          this.stack.push(Math.trunc(Date.now()));
          break;

        case OpCode.TimeUnixUs:
          this.stack.push(Math.trunc(Date.now() * 1000));
          break;

        case OpCode.TimeMonoNs:
          this.stack.push(Math.trunc((performance.now() - this.monoOriginMs) * 1_000_000));
          break;

        case OpCode.TimeMonoTicks:
          this.stack.push(Math.trunc((performance.now() - this.monoOriginMs) * 1000));
          break;

        case OpCode.TimeMonoTicksPerSecond:
          this.stack.push(1_000_000);
          break;

        case OpCode.HostCall: {
          const symbol = this.readStringOperand();
          const argCount = this.readIntOperand();
          const binding = this.hostBindings.get(symbol);
          if (!binding) {
            this.throwRuntime(`Missing host binding '${symbol}'`, "HostBindingError");
          }
          if (binding.arity !== argCount) {
            this.throwRuntime(
              `Host binding '${symbol}' expects ${binding.arity} args, got ${argCount}`,
              "HostBindingError"
            );
          }

          this.ensureStack(argCount);
          const args = new Array(argCount);
          for (let i = argCount - 1; i >= 0; i -= 1) {
            args[i] = this.stack.pop();
          }

          const result = binding.handler(args);
          this.stack.push(result ?? 0);
          break;
        }

        case OpCode.Halt:
          return;

        default:
          this.throwRuntime(`Unknown opcode ${op} at ${this.ip - 1}`);
      }
    }
  }

  numericBinary(operation) {
    const b = this.popNumber();
    const a = this.popNumber();
    this.stack.push(operation(a, b));
  }

  popNumber() {
    this.ensureStack(1);
    const value = this.stack.pop();
    return toNumber(value, message => this.throwRuntime(message));
  }

  popAny2() {
    this.ensureStack(2);
    const b = this.stack.pop();
    const a = this.stack.pop();
    return [a, b];
  }

  ensureStack(needed) {
    if (this.stack.length < needed) {
      this.throwRuntime(`Stack underflow (need ${needed}, have ${this.stack.length})`);
    }
  }

  ensureLocals(index) {
    if (index < 0) {
      this.throwRuntime(`Negative local index ${index}`);
    }
    while (index >= this.locals.length) {
      this.locals.push(0);
    }
  }

  ensureBytes(count) {
    if (this.ip + count > this.codeEnd) {
      this.throwRuntime("Unexpected end of bytecode while reading operand.");
    }
  }

  readIntOperand() {
    this.ensureBytes(4);
    const value = this.view.getInt32(this.ip, true);
    this.ip += 4;
    return value;
  }

  readStringOperand() {
    const length = this.readIntOperand();
    this.ensureBytes(length);
    const value = Utf8Decoder.decode(this.bytes.subarray(this.ip, this.ip + length));
    this.ip += length;
    return value;
  }

  readInterfaceDispatchTable() {
    const explicitArgCount = this.readIntOperand();
    const entryCount = this.readIntOperand();
    const entries = new Map();
    for (let i = 0; i < entryCount; i += 1) {
      const runtimeTypeName = this.readStringOperand();
      const targetIp = this.readIntOperand();
      const localCount = this.readIntOperand();
      entries.set(runtimeTypeName, { targetIp, localCount });
    }
    return {
      nextIp: this.ip,
      explicitArgCount,
      entries
    };
  }

  throwRuntime(message, type = "RuntimeError") {
    const frames = [];
    for (let i = this.callStack.length - 1; i >= 0; i -= 1) {
      const frame = this.callStack[i];
      const loc = this.debugMap.get(frame.callIp);
      frames.push({
        ip: frame.callIp,
        line: loc ? loc.line : -1,
        column: loc ? loc.column : -1
      });
    }

    const faultIp = this.ip - 1;
    const faultLoc = this.debugMap.get(faultIp);
    const line = faultLoc ? faultLoc.line : -1;
    const column = faultLoc ? faultLoc.column : -1;
    const errorObject = {
      __vmError: true,
      type,
      message,
      line,
      column,
      frames
    };

    throw new VmRuntimeError(message, {
      ip: faultIp,
      line,
      column,
      callStack: frames,
      error: errorObject
    });
  }
}
