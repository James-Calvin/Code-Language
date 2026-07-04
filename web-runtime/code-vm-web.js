const BYTECODE_MAGIC = "CODE";
const BYTECODE_VERSION = 11;
const HEADER_SIZE = 13;
const DEBUG_ENTRY_SIZE = 12;

const OptionalNone = Symbol("optional-none");

const OpCode = {
  PushConst: 0x01,
  Add: 0x02,
  Sub: 0x03,
  Mul: 0x04,
  Div: 0x05,
  IntDiv: 0x49,
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
  ArrayAppend: 0x2b,
  ArrayRemoveAt: 0x2c,
  NewMap: 0x2d,
  MapGet: 0x2e,
  MapSet: 0x2f,
  MapContains: 0x30,
  MapRemove: 0x31,
  NewSet: 0x32,
  SetAdd: 0x33,
  SetContains: 0x34,
  SetRemove: 0x35,
  NewQueue: 0x36,
  QueueEnqueue: 0x37,
  QueueDequeue: 0x38,
  QueuePeek: 0x39,
  NewStack: 0x3a,
  StackPush: 0x3b,
  StackPop: 0x3c,
  StackPeek: 0x3d,
  NewRecord: 0x3e,
  FallibleSuccess: 0x3f,
  FallibleError: 0x40,
  FallibleIsError: 0x41,
  FallibleValue: 0x42,
  FallibleErrorCode: 0x43,
  FallibleErrorMessage: 0x44,
  PushReal: 0x45,
  CastInteger: 0x46,
  CastWhole: 0x47,
  CastReal: 0x48,
  PushWideInteger: 0x4a,
  CheckedSizedNumericCast: 0x4b,
  LoadGlobal: 0x4c,
  StoreGlobal: 0x4d,
  Halt: 0xff
};

const OpCodeNames = Object.fromEntries(
  Object.entries(OpCode).map(([name, value]) => [value, name]));

const SizedNumericKind = {
  Integer8: 1,
  Integer16: 2,
  Integer32: 3,
  Whole8: 4,
  Whole16: 5,
  Whole32: 6,
  Real32: 7
};

function sizedNumericName(kind) {
  switch (kind) {
    case SizedNumericKind.Integer8: return "integer8";
    case SizedNumericKind.Integer16: return "integer16";
    case SizedNumericKind.Integer32: return "integer32";
    case SizedNumericKind.Whole8: return "whole8";
    case SizedNumericKind.Whole16: return "whole16";
    case SizedNumericKind.Whole32: return "whole32";
    case SizedNumericKind.Real32: return "real32";
    default: return `unknown sized numeric kind ${kind}`;
  }
}

function sizedNumericIntegralRange(kind) {
  switch (kind) {
    case SizedNumericKind.Integer8: return [-128, 127];
    case SizedNumericKind.Integer16: return [-32768, 32767];
    case SizedNumericKind.Integer32: return [-2147483648, 2147483647];
    case SizedNumericKind.Whole8: return [0, 255];
    case SizedNumericKind.Whole16: return [0, 65535];
    case SizedNumericKind.Whole32: return [0, 4294967295];
    default: throw new Error(`Sized numeric kind '${kind}' is not integral.`);
  }
}

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
  const debugEnd = codeEnd + debugCount * DEBUG_ENTRY_SIZE;
  if (debugEnd + 8 > bytes.length) {
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

  const metadata = readMetadata(bytes, view, debugEnd, codeEnd);
  return { codeEnd, debugMap, view, metadata };
}

function readMetadata(bytes, view, offset, codeEnd) {
  const magic = String.fromCharCode(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
  if (magic !== "META") throw new Error("Bytecode metadata magic is missing.");
  const payloadSize = view.getInt32(offset + 4, true);
  const end = offset + 8 + payloadSize;
  if (payloadSize < 0 || end !== bytes.length) throw new Error("Bytecode metadata size is invalid.");
  let cursor = offset + 8;
  const ensure = count => {
    if (count < 0 || cursor + count > end) throw new Error("Bytecode metadata is truncated.");
  };
  const readInt = name => {
    ensure(4);
    const value = view.getInt32(cursor, true);
    cursor += 4;
    if (value < 0) throw new Error(`Bytecode metadata ${name} is negative.`);
    return value;
  };
  const readIndex = (count, name) => {
    const value = readInt(name);
    if (value >= count) throw new Error(`Bytecode metadata ${name} is out of range.`);
    return value;
  };
  const stringCount = readInt("string count");
  const strings = [];
  for (let i = 0; i < stringCount; i += 1) {
    const length = readInt("string length");
    ensure(length);
    strings.push(Utf8Decoder.decode(bytes.subarray(cursor, cursor + length)));
    cursor += length;
  }
  const fieldCount = readInt("field count");
  const fields = [];
  for (let i = 0; i < fieldCount; i += 1) fields.push(strings[readIndex(strings.length, "field string")]);
  const hostCount = readInt("host binding count");
  const hostBindings = [];
  for (let i = 0; i < hostCount; i += 1) {
    hostBindings.push({ symbol: strings[readIndex(strings.length, "host symbol")], arity: readInt("host arity") });
  }
  const typeCount = readInt("type count");
  const types = [];
  for (let i = 0; i < typeCount; i += 1) {
    const name = strings[readIndex(strings.length, "type name")];
    ensure(1);
    const kind = bytes[cursor++];
    if (kind !== 0 && kind !== 1) throw new Error("Bytecode type kind is invalid.");
    const declaredCount = readInt("declared field count");
    const fieldSlots = [];
    for (let field = 0; field < declaredCount; field += 1) fieldSlots.push(readIndex(fields.length, "field slot"));
    const hashCount = readInt("hash field count");
    const hashFieldSlots = [];
    for (let field = 0; field < hashCount; field += 1) hashFieldSlots.push(readIndex(fields.length, "hash field slot"));
    types.push({ name, isRecord: kind === 1, fieldSlots, hashFieldSlots });
  }
  const callableCount = readInt("callable count");
  const callables = [];
  const functionNames = new Map();
  for (let i = 0; i < callableCount; i += 1) {
    const targetIp = readInt("callable target");
    const frameSize = readInt("callable frame size");
    const name = strings[readIndex(strings.length, "callable name")];
    if (targetIp < HEADER_SIZE || targetIp >= codeEnd) throw new Error("Bytecode callable target is outside code.");
    callables.push({ targetIp, frameSize, name });
    functionNames.set(targetIp, name);
  }
  if (cursor !== end) throw new Error("Bytecode metadata has trailing data.");
  return { strings, fields, hostBindings, types, callables, functionNames };
}

function decodeInstructions(bytes, view, codeEnd, metadata) {
  const decoded = new Array(codeEnd);
  let ip = HEADER_SIZE;
  const ensure = count => {
    if (ip + count > codeEnd) throw new Error("Unexpected end of bytecode while decoding instruction.");
  };
  const readInt = () => { ensure(4); const value = view.getInt32(ip, true); ip += 4; return value; };
  while (ip < codeEnd) {
    const byteIp = ip;
    const op = bytes[ip++];
    let a = 0;
    let b = 0;
    let c = 0;
    let extra = null;
    switch (op) {
      case OpCode.PushConst:
      case OpCode.PushString:
      case OpCode.Jump:
      case OpCode.JumpIfZero:
      case OpCode.JumpIfNotZero:
      case OpCode.Load:
      case OpCode.Store:
      case OpCode.NewArray:
      case OpCode.NewObject:
      case OpCode.GetField:
      case OpCode.SetField:
      case OpCode.HostCall:
      case OpCode.NewRecord:
      case OpCode.LoadGlobal:
      case OpCode.StoreGlobal:
        a = readInt();
        break;
      case OpCode.PushReal:
        ensure(8); a = view.getFloat64(ip, true); ip += 8;
        break;
      case OpCode.PushWideInteger: {
        ensure(8);
        const low = view.getUint32(ip, true);
        const high = view.getInt32(ip + 4, true);
        a = (high * 4294967296) + low;
        ip += 8;
        break;
      }
      case OpCode.CheckedSizedNumericCast:
        ensure(1); a = bytes[ip++];
        break;
      case OpCode.Call:
        a = readInt(); b = readInt(); c = readInt();
        break;
      case OpCode.InterfaceCall: {
        a = readInt();
        const entryCount = readInt();
        extra = new Map();
        for (let entry = 0; entry < entryCount; entry += 1) {
          const typeId = readInt();
          const targetIp = readInt();
          const localCount = readInt();
          extra.set(typeId, { targetIp, localCount });
        }
        break;
      }
      default:
        break;
    }
    if (op === OpCode.PushString && (a < 0 || a >= metadata.strings.length)) throw new Error("Bytecode string ID is out of range.");
    if ((op === OpCode.NewObject || op === OpCode.NewRecord) && (a < 0 || a >= metadata.types.length)) throw new Error("Bytecode type ID is out of range.");
    if ((op === OpCode.GetField || op === OpCode.SetField) && (a < 0 || a >= metadata.fields.length)) throw new Error("Bytecode field slot is out of range.");
    if (op === OpCode.HostCall && (a < 0 || a >= metadata.hostBindings.length)) throw new Error("Bytecode host binding ID is out of range.");
    if (op === OpCode.InterfaceCall) {
      for (const typeId of extra.keys()) if (typeId < 0 || typeId >= metadata.types.length) throw new Error("Bytecode interface type ID is out of range.");
    }
    decoded[byteIp] = { op, a, b, c, extra, byteIp, nextIp: ip };
  }
  return decoded;
}

class RuntimeProfiler {
  constructor(vm, enabled = false) {
    this.vm = vm;
    this.enabled = enabled;
    this.reset();
  }

  start() {
    this.reset();
    this.enabled = true;
    this.startedAtMs = performance.now();
    return this;
  }

  stop() {
    if (this.enabled) {
      this.elapsedMs += performance.now() - this.startedAtMs;
      this.enabled = false;
    }
    return this.report();
  }

  reset() {
    this.startedAtMs = performance.now();
    this.elapsedMs = 0;
    this.instructionCount = 0;
    this.opcodeCounts = new Uint32Array(256);
    this.functionStats = new Map();
    this.functionFrames = [];
    this.hostStats = new Map();
    this.allocations = { objects: 0, arrays: 0, callFrames: 0 };
    this.maxStackDepth = 0;
    this.maxCallDepth = 0;
    return this;
  }

  instruction(op) {
    if (!this.enabled) return;
    this.instructionCount += 1;
    this.opcodeCounts[op] += 1;
    this.maxStackDepth = Math.max(this.maxStackDepth, this.vm.stack.length);
    this.maxCallDepth = Math.max(this.maxCallDepth, this.vm.callStack.length);
  }

  allocate(kind) {
    if (this.enabled) this.allocations[kind] += 1;
  }

  enterFunction(targetIp) {
    if (!this.enabled) return;
    this.functionFrames.push({ targetIp, startedAtMs: performance.now(), childMs: 0 });
  }

  leaveFunction() {
    if (!this.enabled || this.functionFrames.length === 0) return;
    const frame = this.functionFrames.pop();
    const inclusiveMs = performance.now() - frame.startedAtMs;
    const stat = this.functionStats.get(frame.targetIp) ?? { calls: 0, inclusiveMs: 0, selfMs: 0 };
    stat.calls += 1;
    stat.inclusiveMs += inclusiveMs;
    stat.selfMs += inclusiveMs - frame.childMs;
    this.functionStats.set(frame.targetIp, stat);
    const parent = this.functionFrames[this.functionFrames.length - 1];
    if (parent) parent.childMs += inclusiveMs;
  }

  measureHost(symbol, handler) {
    if (!this.enabled) return handler();
    const startedAtMs = performance.now();
    try {
      return handler();
    } finally {
      const stat = this.hostStats.get(symbol) ?? { calls: 0, milliseconds: 0 };
      stat.calls += 1;
      stat.milliseconds += performance.now() - startedAtMs;
      this.hostStats.set(symbol, stat);
    }
  }

  sourceLabel(ip) {
    const name = this.vm.functionNames.get(ip);
    const exact = this.vm.debugMap.get(ip);
    if (exact) return `${name ?? `ip ${ip}`} (${exact.line}:${exact.column})`;
    let nearestIp = -1;
    let nearest = null;
    for (const [candidateIp, location] of this.vm.debugMap) {
      if (candidateIp <= ip && candidateIp > nearestIp) {
        nearestIp = candidateIp;
        nearest = location;
      }
    }
    return nearest ? `${name ?? `ip ${ip}`} (${nearest.line}:${nearest.column})` : (name ?? `ip ${ip}`);
  }

  report() {
    const liveElapsedMs = this.enabled ? performance.now() - this.startedAtMs : 0;
    const opcodes = [];
    for (let op = 0; op < this.opcodeCounts.length; op += 1) {
      if (this.opcodeCounts[op] > 0) {
        opcodes.push({ opcode: OpCodeNames[op] ?? `0x${op.toString(16)}`, count: this.opcodeCounts[op] });
      }
    }
    opcodes.sort((a, b) => b.count - a.count);
    const functions = [...this.functionStats.entries()].map(([ip, stat]) => ({
      function: this.sourceLabel(ip), ...stat
    })).sort((a, b) => b.inclusiveMs - a.inclusiveMs);
    const hostCalls = [...this.hostStats.entries()].map(([symbol, stat]) => ({
      symbol, ...stat
    })).sort((a, b) => b.milliseconds - a.milliseconds);
    return {
      enabled: this.enabled,
      elapsedMs: this.elapsedMs + liveElapsedMs,
      instructionCount: this.instructionCount,
      instructionsPerMillisecond: this.instructionCount / Math.max(0.001, this.elapsedMs + liveElapsedMs),
      opcodes,
      functions,
      hostCalls,
      allocations: { ...this.allocations },
      runtimeLayout: {
        decodedInstructions: this.vm.decodedInstructionCount,
        pooledStrings: this.vm.metadata.strings.length,
        fieldSlots: this.vm.metadata.fields.length,
        hostBindings: this.vm.metadata.hostBindings.length,
        types: this.vm.metadata.types.length,
        callables: this.vm.metadata.callables.length,
        localsHighWater: this.vm.localsHighWater
      },
      maxStackDepth: this.maxStackDepth,
      maxCallDepth: this.maxCallDepth
    };
  }

  print() {
    const report = this.report();
    console.log("Code runtime profile", report);
    console.table(report.opcodes);
    console.table(report.functions);
    console.table(report.hostCalls);
    return report;
  }
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

function toText(value, fail) {
  if (typeof value === "string") {
    return value;
  }
  fail(`Expected string on stack, found ${typeof value}`);
  return "";
}

function toNumberArray(value, fail) {
  if (!Array.isArray(value)) {
    fail(`Expected array on stack, found ${typeof value}`);
    return [];
  }

  const result = new Array(value.length);
  for (let i = 0; i < value.length; i += 1) {
    result[i] = toNumber(value[i], fail);
  }
  return result;
}

function isVmQueue(value) {
  return value && typeof value === "object" && value.__vmQueue === true && Array.isArray(value.items);
}

function isVmStack(value) {
  return value && typeof value === "object" && value.__vmStack === true && Array.isArray(value.items);
}

function isVmObject(value) {
  return value !== null && typeof value === "object" && value.__vmObject === true;
}

function isVmMap(value) {
  return value && typeof value === "object" && value.__vmMap === true && value.buckets instanceof Map;
}

function isVmSet(value) {
  return value && typeof value === "object" && value.__vmSet === true && value.buckets instanceof Map;
}

function isVmFallible(value) {
  return value && typeof value === "object" && value.__vmFallible === true;
}

function createVmObject(typeId, type, fieldCount) {
  return {
    __vmObject: true,
    typeId,
    typeName: type.name,
    isRecord: type.isRecord,
    fields: new Array(fieldCount).fill(0),
    initializedFields: new Uint8Array(fieldCount),
    hashFieldSlots: type.hashFieldSlots ?? []
  };
}

function createVmMap() {
  return { __vmMap: true, buckets: new Map(), size: 0 };
}

function createVmSet() {
  return { __vmSet: true, buckets: new Map(), size: 0 };
}

function createFallibleSuccess(value) {
  return { __vmFallible: true, isError: false, value, code: 0, message: "" };
}

function createFallibleError(code, message) {
  return { __vmFallible: true, isError: true, value: 0, code, message: String(message ?? "") };
}

function stringHash(value) {
  let hash = 2166136261;
  for (let i = 0; i < value.length; i += 1) {
    hash ^= value.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return hash | 0;
}

function combineHash(hash, part) {
  return Math.imul(hash ^ part, 16777619) | 0;
}

function valueEquals(left, right) {
  if (left === right) {
    return true;
  }

  if (left == null || right == null) {
    return false;
  }

  if (isNumberValue(left) && isNumberValue(right)) {
    return left === right;
  }

  if (isVmObject(left) && isVmObject(right)) {
    if (left.isRecord || right.isRecord) {
      if (!left.isRecord || !right.isRecord) {
        return false;
      }
      if (left.typeId !== right.typeId) {
        return false;
      }
      const hashSlots = left.hashFieldSlots ?? [];
      for (const slot of hashSlots) {
        if (left.initializedFields[slot] !== right.initializedFields[slot]) {
          return false;
        }
        if (left.initializedFields[slot] && !valueEquals(left.fields[slot], right.fields[slot])) {
          return false;
        }
      }

      return true;
    }

    return false;
  }

  if (Array.isArray(left) && Array.isArray(right)) {
    return orderedValuesEqual(left, right);
  }

  if (isVmQueue(left) && isVmQueue(right)) {
    return orderedValuesEqual(left.items, right.items);
  }

  if (isVmStack(left) && isVmStack(right)) {
    return orderedValuesEqual([...left.items].reverse(), [...right.items].reverse());
  }

  if (isVmSet(left) && isVmSet(right)) {
    return vmSetsEqual(left, right);
  }

  if (isVmMap(left) && isVmMap(right)) {
    return vmMapsEqual(left, right);
  }

  return left === right;
}

function valueHash(value) {
  if (value == null) {
    return 0;
  }

  if (isNumberValue(value)) {
    return stringHash(String(value));
  }

  if (typeof value === "string") {
    return stringHash(`s:${value}`);
  }

  if (typeof value === "boolean") {
    return value ? 1 : 0;
  }

  if (value === OptionalNone) {
    return stringHash("optional:none");
  }

  if (isVmObject(value)) {
    if (!value.isRecord) {
      if (!Object.prototype.hasOwnProperty.call(value, "__identityHash")) {
        value.__identityHash = stringHash(`${value.typeName}:${Math.random()}:${performance.now()}`);
      }
      return value.__identityHash;
    }

    let hash = combineHash(2166136261, stringHash(`record:${value.typeName}`));
    const hashSlots = value.hashFieldSlots ?? [];
    for (const slot of hashSlots) {
      if (!value.initializedFields[slot]) continue;
      hash = combineHash(hash, slot);
      hash = combineHash(hash, valueHash(value.fields[slot]));
    }
    return hash;
  }

  if (Array.isArray(value)) {
    return orderedHash("array", value);
  }

  if (isVmQueue(value)) {
    return orderedHash("queue", value.items);
  }

  if (isVmStack(value)) {
    return orderedHash("stack", [...value.items].reverse());
  }

  if (isVmSet(value)) {
    let entriesHash = 0;
    for (const entry of iterateSetEntries(value)) entriesHash = (entriesHash + valueHash(entry)) | 0;
    return combineHash(combineHash(stringHash("set"), value.size | 0), entriesHash);
  }

  if (isVmMap(value)) {
    let entriesHash = 0;
    for (const entry of iterateMapEntries(value)) {
      entriesHash = (entriesHash + combineHash(valueHash(entry.key), valueHash(entry.value))) | 0;
    }
    return combineHash(combineHash(stringHash("map"), value.size | 0), entriesHash);
  }

  if (!Object.prototype.hasOwnProperty.call(value, "__identityHash")) {
    Object.defineProperty(value, "__identityHash", {
      value: stringHash(`${Object.prototype.toString.call(value)}:${Math.random()}:${performance.now()}`),
      enumerable: false,
      configurable: false,
      writable: false
    });
  }

  return value.__identityHash;
}

function orderedValuesEqual(left, right) {
  if (left.length !== right.length) return false;
  for (let index = 0; index < left.length; index += 1) {
    if (!valueEquals(left[index], right[index])) return false;
  }
  return true;
}

function orderedHash(kind, values) {
  let hash = stringHash(kind);
  for (const value of values) hash = combineHash(hash, valueHash(value));
  return hash;
}

function* iterateMapEntries(map) {
  for (const entries of map.buckets.values()) {
    for (const entry of entries) yield entry;
  }
}

function* iterateSetEntries(set) {
  for (const entries of set.buckets.values()) {
    for (const entry of entries) yield entry;
  }
}

function vmMapsEqual(left, right) {
  if (left.size !== right.size) return false;
  for (const entry of iterateMapEntries(left)) {
    const candidate = vmMapTryGet(right, entry.key);
    if (!candidate.found || !valueEquals(entry.value, candidate.value)) return false;
  }
  return true;
}

function vmSetsEqual(left, right) {
  if (left.size !== right.size) return false;
  for (const entry of iterateSetEntries(left)) {
    if (!vmSetContains(right, entry)) return false;
  }
  return true;
}

function snapshotHashKey(value, seen = new Map()) {
  if (value == null || isNumberValue(value) || typeof value === "string" || typeof value === "boolean" || value === OptionalNone) {
    return value;
  }

  if (isVmObject(value)) {
    if (!value.isRecord) return value;
    if (seen.has(value)) return seen.get(value);
    const clone = {
      __vmObject: true,
      typeId: value.typeId,
      typeName: value.typeName,
      isRecord: true,
      fields: new Array(value.fields.length).fill(0),
      initializedFields: new Uint8Array(value.initializedFields.length),
      hashFieldSlots: value.hashFieldSlots ?? []
    };
    seen.set(value, clone);
    for (let slot = 0; slot < value.fields.length; slot += 1) {
      if (!value.initializedFields[slot]) continue;
      clone.fields[slot] = snapshotHashKey(value.fields[slot], seen);
      clone.initializedFields[slot] = 1;
    }
    return clone;
  }

  if (Array.isArray(value)) {
    if (seen.has(value)) return seen.get(value);
    const clone = [];
    seen.set(value, clone);
    for (const item of value) clone.push(snapshotHashKey(item, seen));
    return clone;
  }

  if (isVmQueue(value)) {
    if (seen.has(value)) return seen.get(value);
    const clone = { __vmQueue: true, items: [] };
    seen.set(value, clone);
    for (const item of value.items) clone.items.push(snapshotHashKey(item, seen));
    return clone;
  }

  if (isVmStack(value)) {
    if (seen.has(value)) return seen.get(value);
    const clone = { __vmStack: true, items: [] };
    seen.set(value, clone);
    for (const item of value.items) clone.items.push(snapshotHashKey(item, seen));
    return clone;
  }

  if (isVmSet(value)) {
    if (seen.has(value)) return seen.get(value);
    const clone = createVmSet();
    seen.set(value, clone);
    for (const entry of iterateSetEntries(value)) vmSetAdd(clone, snapshotHashKey(entry, seen));
    return clone;
  }

  if (isVmMap(value)) {
    if (seen.has(value)) return seen.get(value);
    const clone = createVmMap();
    seen.set(value, clone);
    for (const entry of iterateMapEntries(value)) vmMapSet(clone, snapshotHashKey(entry.key, seen), snapshotHashKey(entry.value, seen));
    return clone;
  }

  return value;
}

function getBucketEntries(container, key, createIfMissing = false) {
  const hash = valueHash(key).toString();
  let entries = container.buckets.get(hash);
  if (!entries && createIfMissing) {
    entries = [];
    container.buckets.set(hash, entries);
  }
  return entries;
}

function vmMapTryGet(map, key) {
  const entries = getBucketEntries(map, key, false);
  if (!entries) {
    return { found: false, value: 0 };
  }

  for (const entry of entries) {
    if (valueEquals(entry.key, key)) {
      return { found: true, value: entry.value };
    }
  }

  return { found: false, value: 0 };
}

function vmMapSet(map, key, value) {
  key = snapshotHashKey(key);
  const entries = getBucketEntries(map, key, true);
  for (const entry of entries) {
    if (valueEquals(entry.key, key)) {
      entry.value = value;
      return;
    }
  }

  entries.push({ key, value });
  map.size += 1;
}

function vmMapContains(map, key) {
  return vmMapTryGet(map, key).found;
}

function vmMapRemove(map, key) {
  const entries = getBucketEntries(map, key, false);
  if (!entries) {
    return false;
  }

  for (let i = 0; i < entries.length; i += 1) {
    if (valueEquals(entries[i].key, key)) {
      entries.splice(i, 1);
      map.size -= 1;
      return true;
    }
  }

  return false;
}

function vmSetAdd(set, value) {
  value = snapshotHashKey(value);
  const entries = getBucketEntries(set, value, true);
  for (const entry of entries) {
    if (valueEquals(entry, value)) {
      return;
    }
  }

  entries.push(value);
  set.size += 1;
}

function vmSetContains(set, value) {
  const entries = getBucketEntries(set, value, false);
  if (!entries) {
    return false;
  }

  for (const entry of entries) {
    if (valueEquals(entry, value)) {
      return true;
    }
  }

  return false;
}

function vmSetRemove(set, value) {
  const entries = getBucketEntries(set, value, false);
  if (!entries) {
    return false;
  }

  for (let i = 0; i < entries.length; i += 1) {
    if (valueEquals(entries[i], value)) {
      entries.splice(i, 1);
      set.size -= 1;
      return true;
    }
  }

  return false;
}

function tryGetCollectionLength(value) {
  if (Array.isArray(value)) {
    return value.length;
  }
  if (isVmMap(value) || isVmSet(value)) {
    return value.size;
  }
  if (isVmQueue(value) || isVmStack(value)) {
    return value.items.length;
  }
  return null;
}

function formatVmError(errorObject) {
  const line = Number.isInteger(errorObject.line) ? errorObject.line : -1;
  const column = Number.isInteger(errorObject.column) ? errorObject.column : -1;
  return `${errorObject.type}: ${errorObject.message} at ${line}:${column}`;
}

function isVmError(value) {
  return value && typeof value === "object" && value.__vmError === true;
}

function clampUnit(value) {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.min(1, Math.max(0, value));
}

function toCssRgba(r, g, b, a) {
  const red = Math.round(clampUnit(r) * 255);
  const green = Math.round(clampUnit(g) * 255);
  const blue = Math.round(clampUnit(b) * 255);
  return `rgba(${red}, ${green}, ${blue}, ${clampUnit(a)})`;
}

function normalizeHorizontalAlignment(value) {
  switch (value) {
    case "left":
    case "center":
    case "right":
      return value;
    default:
      return "left";
  }
}

function normalizeVerticalAlignment(value) {
  switch (value) {
    case "top":
    case "middle":
    case "bottom":
      return value;
    default:
      return "top";
  }
}

export class CanvasSceneRuntime {
  constructor(options = {}) {
    this.virtualWidth = Math.max(1, Math.trunc(options.width || 640));
    this.virtualHeight = Math.max(1, Math.trunc(options.height || 360));
    this.safeLeft = 0;
    this.safeTop = 0;
    this.safeWidth = this.virtualWidth;
    this.safeHeight = this.virtualHeight;
    this.viewLeft = 0;
    this.viewTop = 0;
    this.viewWidth = this.virtualWidth;
    this.viewHeight = this.virtualHeight;
    this.viewportWidth = this.virtualWidth;
    this.viewportHeight = this.virtualHeight;
    this.worldScale = 1;
    this.devicePixelRatio = 1;
    this.drawSpace = "world";
    this.title = typeof options.title === "string" && options.title.length > 0
      ? options.title
      : "Code App";
    this.keysDown = new Set();
    this.pointerScreenX = 0;
    this.pointerScreenY = 0;
    this.pointerIsDownNow = false;
    this.pointerActiveId = null;
    this.pointerPressedPending = false;
    this.pointerReleasedPending = false;
    this.pointerWasPressedForStep = false;
    this.pointerWasReleasedForStep = false;
    this.updateMode = "fixed";
    this.fixedUpdatesPerSecond = 60;
    this.stepMs = 1000 / this.fixedUpdatesPerSecond;
    this.continuousUpdateChunkMs = 4;
    this.maximumRenderRate = 0;
    this.accumulatorMs = 0;
    this.lastTimestampMs = 0;
    this.lastDrawTimestampMs = 0;
    this.lastUpdateTimestampMs = 0;
    this.lastUpdateIntervalMs = 0;
    this.lastUpdateDeltaMs = 1000 / 60;
    this.lastUpdatePumpTimestampMs = 0;
    this.pendingUpdateWorkMs = 0;
    this.pendingUpdateSteps = 0;
    this.pendingDroppedUpdateSteps = 0;
    this.updateTimerHandle = 0;
    this.documentHidden = false;
    this.running = false;
    this.frameHandle = 0;
    this.sceneObject = null;
    this.sceneInfo = null;
    this.vm = null;
    this.lastFrameIntervalMs = 0;
    this.lastFrameWorkMs = 0;
    this.lastUpdateWorkMs = 0;
    this.lastFrameUpdateWorkMs = 0;
    this.lastDrawWorkMs = 0;
    this.lastDrawHudWorkMs = 0;
    this.lastUpdateStepsCount = 0;
    this.lastDroppedUpdateStepsCount = 0;
    this.maxUpdateStepsPerFrame = 5;
    this.appControlKeyCodes = new Set([32, 33, 34, 35, 36, 37, 38, 39, 40]);
    this.canvas = null;
    this.ctx = null;
    this.outputElement = null;
    this.imageCache = new Map();
    this.audioHandles = new Map();
    this.pendingAudioHandles = new Set();
    this.nextAudioHandle = 1;
    this.audioUnlocked = false;
    this.workerController = null;
    this.audioStatusChanged = null;
    this.handleResize = () => this.resize();
    this.handleKeyDown = event => {
      if (this.shouldPreventBrowserKeyDefault(event)) {
        event.preventDefault();
      }
      this.unlockAudio();
      this.keysDown.add(event.keyCode);
      this.notifyWorkerInput();
    };
    this.handleKeyUp = event => {
      if (this.shouldPreventBrowserKeyDefault(event)) {
        event.preventDefault();
      }
      this.keysDown.delete(event.keyCode);
      this.notifyWorkerInput();
    };
    this.handleBlur = () => {
      this.keysDown.clear();
      this.cancelActivePointer();
      this.notifyWorkerInput();
    };
    this.handlePointerDown = event => this.onPointerDown(event);
    this.handlePointerMove = event => this.onPointerMove(event);
    this.handlePointerUp = event => this.onPointerUp(event);
    this.handlePointerCancel = event => this.onPointerCancel(event);
    this.handleContextMenu = event => event.preventDefault();
    this.tick = timestamp => this.onFrame(timestamp);
    this.updateChannel = typeof MessageChannel === "function" ? new MessageChannel() : null;
    if (this.updateChannel) {
      this.updateChannel.port1.onmessage = () => this.onUpdatePump();
      if (typeof this.updateChannel.port1.unref === "function") this.updateChannel.port1.unref();
      if (typeof this.updateChannel.port2.unref === "function") this.updateChannel.port2.unref();
    }
    this.handleVisibilityChange = () => this.onVisibilityChange();
  }

  attach(root = document.body) {
    if (this.canvas) {
      return;
    }

    document.title = this.title;
    document.documentElement.style.margin = "0";
    document.documentElement.style.width = "100%";
    document.documentElement.style.height = "100%";
    document.documentElement.style.overflow = "hidden";

    root.style.margin = "0";
    root.style.width = "100%";
    root.style.height = "100%";
    root.style.overflow = "hidden";
    root.style.position = "relative";
    root.style.display = "block";
    root.style.background = "#000000";

    const canvas = document.createElement("canvas");
    canvas.width = this.virtualWidth;
    canvas.height = this.virtualHeight;
    canvas.style.display = "block";
    canvas.style.position = "absolute";
    canvas.style.left = "0";
    canvas.style.top = "0";
    canvas.style.width = "100%";
    canvas.style.height = "100%";
    canvas.style.background = "transparent";
    canvas.style.touchAction = "none";
    canvas.style.userSelect = "none";

    const output = document.createElement("pre");
    output.style.position = "absolute";
    output.style.left = "12px";
    output.style.bottom = "12px";
    output.style.margin = "0";
    output.style.maxWidth = "min(560px, calc(100vw - 24px))";
    output.style.maxHeight = "40vh";
    output.style.overflow = "auto";
    output.style.padding = "10px 12px";
    output.style.borderRadius = "10px";
    output.style.background = "rgba(9, 12, 19, 0.78)";
    output.style.color = "#e2e8f0";
    output.style.font = "12px/1.4 Consolas, 'Courier New', monospace";
    output.style.whiteSpace = "pre-wrap";
    output.style.display = "none";

    root.replaceChildren(canvas, output);

    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.outputElement = output;
    if (this.ctx) {
      this.ctx.imageSmoothingEnabled = false;
    }

    window.addEventListener("resize", this.handleResize);
    window.addEventListener("keydown", this.handleKeyDown, { passive: false });
    window.addEventListener("keyup", this.handleKeyUp, { passive: false });
    window.addEventListener("blur", this.handleBlur);
    document.addEventListener("visibilitychange", this.handleVisibilityChange);
    canvas.addEventListener("pointerdown", this.handlePointerDown, { passive: false });
    canvas.addEventListener("pointermove", this.handlePointerMove, { passive: false });
    canvas.addEventListener("pointerup", this.handlePointerUp, { passive: false });
    canvas.addEventListener("pointercancel", this.handlePointerCancel, { passive: false });
    canvas.addEventListener("contextmenu", this.handleContextMenu);
    this.resize();
  }

  dispose() {
    this.stop();
    this.stopAllSounds();
    this.audioHandles.clear();
    this.pendingAudioHandles.clear();
    if (this.canvas) {
      this.canvas.removeEventListener("pointerdown", this.handlePointerDown);
      this.canvas.removeEventListener("pointermove", this.handlePointerMove);
      this.canvas.removeEventListener("pointerup", this.handlePointerUp);
      this.canvas.removeEventListener("pointercancel", this.handlePointerCancel);
      this.canvas.removeEventListener("contextmenu", this.handleContextMenu);
    }
    window.removeEventListener("resize", this.handleResize);
    window.removeEventListener("keydown", this.handleKeyDown);
    window.removeEventListener("keyup", this.handleKeyUp);
    window.removeEventListener("blur", this.handleBlur);
    this.keysDown.clear();
    this.cancelActivePointer();
  }

  resize() {
    if (!this.canvas || !this.ctx) {
      return;
    }

    const viewportWidth = Math.max(1, window.innerWidth || document.documentElement.clientWidth || this.virtualWidth);
    const viewportHeight = Math.max(1, window.innerHeight || document.documentElement.clientHeight || this.virtualHeight);
    const viewportAspect = viewportWidth / viewportHeight;
    const safeAspect = this.virtualWidth / this.virtualHeight;

    this.viewportWidth = viewportWidth;
    this.viewportHeight = viewportHeight;
    this.devicePixelRatio = Math.max(1, window.devicePixelRatio || 1);

    if (viewportAspect >= safeAspect) {
      this.viewHeight = this.virtualHeight;
      this.viewWidth = this.viewHeight * viewportAspect;
    } else {
      this.viewWidth = this.virtualWidth;
      this.viewHeight = this.viewWidth / viewportAspect;
    }

    this.viewLeft = (this.virtualWidth - this.viewWidth) / 2;
    this.viewTop = (this.virtualHeight - this.viewHeight) / 2;
    this.worldScale = viewportWidth / this.viewWidth;

    this.canvas.width = Math.max(1, Math.round(viewportWidth * this.devicePixelRatio));
    this.canvas.height = Math.max(1, Math.round(viewportHeight * this.devicePixelRatio));
    this.canvas.style.width = `${viewportWidth}px`;
    this.canvas.style.height = `${viewportHeight}px`;
    this.applyCurrentTransform();
    this.workerController?.sendViewportSnapshot();
  }

  notifyWorkerInput() {
    this.workerController?.sendInputSnapshot();
  }

  appendOutput(line) {
    if (!this.outputElement) {
      return;
    }

    this.outputElement.style.display = "block";
    this.outputElement.textContent += `${line}\n`;
    this.outputElement.scrollTop = this.outputElement.scrollHeight;
  }

  shouldPreventBrowserKeyDefault(event) {
    return this.appControlKeyCodes.has(event.keyCode);
  }

  preventPointerDefault(event) {
    if (event.cancelable) {
      event.preventDefault();
    }
  }

  isPrimaryPointerEvent(event) {
    return event.isPrimary !== false;
  }

  isPrimaryPointerDownEvent(event) {
    if (!this.isPrimaryPointerEvent(event)) {
      return false;
    }
    return event.pointerType !== "mouse" || event.button === 0;
  }

  shouldTrackPointerMove(event) {
    if (this.pointerActiveId !== null) {
      return event.pointerId === this.pointerActiveId;
    }
    return this.isPrimaryPointerEvent(event);
  }

  updatePointerPosition(event) {
    if (!this.canvas) {
      return;
    }

    const rect = this.canvas.getBoundingClientRect();
    this.pointerScreenX = event.clientX - rect.left;
    this.pointerScreenY = event.clientY - rect.top;
  }

  onPointerDown(event) {
    if (!this.isPrimaryPointerDownEvent(event)) {
      return;
    }
    if (this.pointerActiveId !== null && event.pointerId !== this.pointerActiveId) {
      return;
    }

    this.unlockAudio();
    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
    this.pointerActiveId = event.pointerId;
    if (!this.pointerIsDownNow) {
      this.pointerPressedPending = true;
    }
    this.pointerIsDownNow = true;

    if (this.canvas && typeof this.canvas.setPointerCapture === "function") {
      try {
        this.canvas.setPointerCapture(event.pointerId);
      } catch {
        // Browsers can reject capture if the pointer is no longer active.
      }
    }
    this.notifyWorkerInput();
  }

  onPointerMove(event) {
    if (!this.shouldTrackPointerMove(event)) {
      return;
    }

    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
    this.notifyWorkerInput();
  }

  onPointerUp(event) {
    if (this.pointerActiveId !== null && event.pointerId !== this.pointerActiveId) {
      return;
    }
    if (this.pointerActiveId === null && !this.isPrimaryPointerEvent(event)) {
      return;
    }

    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
    this.releaseActivePointer(event.pointerId);
    this.notifyWorkerInput();
  }

  onPointerCancel(event) {
    if (this.pointerActiveId !== null && event.pointerId !== this.pointerActiveId) {
      return;
    }

    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
    this.releaseActivePointer(event.pointerId);
    this.notifyWorkerInput();
  }

  releaseActivePointer(pointerId) {
    if (this.pointerIsDownNow) {
      this.pointerReleasedPending = true;
    }
    this.pointerIsDownNow = false;
    this.pointerActiveId = null;

    if (this.canvas && typeof this.canvas.releasePointerCapture === "function") {
      try {
        this.canvas.releasePointerCapture(pointerId);
      } catch {
        // Capture may already be released by the browser.
      }
    }
  }

  cancelActivePointer() {
    const pointerId = this.pointerActiveId;
    if (this.pointerIsDownNow) {
      this.pointerReleasedPending = true;
    }
    this.pointerIsDownNow = false;
    this.pointerActiveId = null;

    if (pointerId !== null && this.canvas && typeof this.canvas.releasePointerCapture === "function") {
      try {
        this.canvas.releasePointerCapture(pointerId);
      } catch {
        // Capture may already be released by the browser.
      }
    }
  }

  beginFixedUpdateStep() {
    this.pointerWasPressedForStep = this.pointerPressedPending;
    this.pointerWasReleasedForStep = this.pointerReleasedPending;
    this.pointerPressedPending = false;
    this.pointerReleasedPending = false;
  }

  showFatal(error) {
    const message = error instanceof VmRuntimeError
      ? `Runtime error: ${error.message}`
      : (error && typeof error.message === "string" ? error.message : String(error));
    this.appendOutput(message);

    if (error instanceof VmRuntimeError) {
      const frames = error.payload?.callStack ?? [];
      if (frames.length > 0) {
        this.appendOutput("Stack trace (most recent call first):");
        for (const frame of frames) {
          this.appendOutput(`  at ip ${frame.ip} (${frame.line}:${frame.column})`);
        }
      }
    }
  }

  clear(r, g, b, a) {
    if (!this.ctx || !this.canvas) {
      return 0;
    }

    this.ctx.save();
    this.ctx.setTransform(1, 0, 0, 1, 0, 0);
    this.ctx.fillStyle = toCssRgba(r, g, b, a);
    this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
    this.ctx.restore();
    return 0;
  }

  drawRect(x, y, w, h, r, g, b, a) {
    return this.drawRectangle(x, y, w, h, r, g, b, a);
  }

  drawRectangle(x, y, w, h, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    this.ctx.fillStyle = toCssRgba(r, g, b, a);
    this.ctx.fillRect(x, y, w, h);
    return 0;
  }

  drawRectangleOutline(x, y, w, h, lineWidth, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    this.ctx.lineWidth = Math.max(1, lineWidth);
    this.ctx.strokeStyle = toCssRgba(r, g, b, a);
    this.ctx.strokeRect(x, y, w, h);
    return 0;
  }

  drawCircle(x, y, radius, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    this.ctx.fillStyle = toCssRgba(r, g, b, a);
    this.ctx.beginPath();
    this.ctx.arc(x, y, Math.max(0, radius), 0, Math.PI * 2);
    this.ctx.fill();
    return 0;
  }

  drawCircleOutline(x, y, radius, lineWidth, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    this.ctx.lineWidth = Math.max(1, lineWidth);
    this.ctx.strokeStyle = toCssRgba(r, g, b, a);
    this.ctx.beginPath();
    this.ctx.arc(x, y, Math.max(0, radius), 0, Math.PI * 2);
    this.ctx.stroke();
    return 0;
  }

  drawPolygon(points, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    return this.drawPolygonPath(points, () => {
      this.ctx.fillStyle = toCssRgba(r, g, b, a);
      this.ctx.fill();
    });
  }

  drawPolygonOutline(points, lineWidth, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    return this.drawPolygonPath(points, () => {
      this.ctx.lineWidth = Math.max(1, lineWidth);
      this.ctx.strokeStyle = toCssRgba(r, g, b, a);
      this.ctx.stroke();
    });
  }

  drawLine(x1, y1, x2, y2, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    this.ctx.strokeStyle = toCssRgba(r, g, b, a);
    this.ctx.beginPath();
    this.ctx.moveTo(x1, y1);
    this.ctx.lineTo(x2, y2);
    this.ctx.stroke();
    return 0;
  }

  drawText(text, x, y, size, horizontalAlignment, verticalAlignment, r, g, b, a) {
    if (!this.ctx) {
      return 0;
    }

    const horizontal = normalizeHorizontalAlignment(horizontalAlignment);
    const vertical = normalizeVerticalAlignment(verticalAlignment);
    this.ctx.fillStyle = toCssRgba(r, g, b, a);
    this.ctx.font = `${Math.max(1, size)}px "Trebuchet MS", Verdana, sans-serif`;
    this.ctx.textAlign = horizontal;
    this.ctx.textBaseline = vertical;
    this.ctx.fillText(text, x, y);
    return 0;
  }

  drawImage(source, x, y, width, height, alpha) {
    if (!this.ctx) {
      return 0;
    }

    const record = this.getImageRecord(source);
    if (!record.loaded || record.failed) {
      return 0;
    }

    this.ctx.save();
    this.ctx.globalAlpha = clampUnit(alpha);
    this.ctx.drawImage(record.image, x, y, width, height);
    this.ctx.restore();
    return 0;
  }

  drawSprite(source, sourceX, sourceY, sourceWidth, sourceHeight, x, y, width, height, alpha) {
    if (!this.ctx) {
      return 0;
    }

    const record = this.getImageRecord(source);
    if (!record.loaded || record.failed) {
      return 0;
    }

    this.ctx.save();
    this.ctx.globalAlpha = clampUnit(alpha);
    this.ctx.drawImage(record.image, sourceX, sourceY, sourceWidth, sourceHeight, x, y, width, height);
    this.ctx.restore();
    return 0;
  }

  keyDown(keyCode) {
    return this.keysDown.has(Math.trunc(keyCode));
  }

  pointerWorldX() {
    return this.pointerScreenX / this.worldScale + this.viewLeft;
  }

  pointerWorldY() {
    return this.pointerScreenY / this.worldScale + this.viewTop;
  }

  pointerScreenXPosition() {
    return this.pointerScreenX;
  }

  pointerScreenYPosition() {
    return this.pointerScreenY;
  }

  pointerIsDown() {
    return this.pointerIsDownNow;
  }

  pointerWasPressed() {
    return this.pointerWasPressedForStep;
  }

  pointerWasReleased() {
    return this.pointerWasReleasedForStep;
  }

  cameraViewLeft() {
    return this.viewLeft;
  }

  cameraViewTop() {
    return this.viewTop;
  }

  cameraViewWidth() {
    return this.viewWidth;
  }

  cameraViewHeight() {
    return this.viewHeight;
  }

  cameraViewRight() {
    return this.viewLeft + this.viewWidth;
  }

  cameraViewBottom() {
    return this.viewTop + this.viewHeight;
  }

  cameraSafeLeft() {
    return this.safeLeft;
  }

  cameraSafeTop() {
    return this.safeTop;
  }

  cameraSafeWidth() {
    return this.safeWidth;
  }

  cameraSafeHeight() {
    return this.safeHeight;
  }

  cameraSafeRight() {
    return this.safeLeft + this.safeWidth;
  }

  cameraSafeBottom() {
    return this.safeTop + this.safeHeight;
  }

  screenWidth() {
    return this.viewportWidth;
  }

  screenHeight() {
    return this.viewportHeight;
  }

  lastFrameIntervalMilliseconds() {
    return this.lastFrameIntervalMs;
  }

  estimatedFramesPerSecond() {
    return this.lastFrameIntervalMs > 0 ? 1000 / this.lastFrameIntervalMs : 0;
  }

  lastFrameWorkMilliseconds() {
    return this.lastFrameWorkMs;
  }

  lastUpdateWorkMilliseconds() {
    return this.lastUpdateWorkMs;
  }

  lastDrawWorkMilliseconds() {
    return this.lastDrawWorkMs;
  }

  lastDrawHudWorkMilliseconds() {
    return this.lastDrawHudWorkMs;
  }

  lastUpdateSteps() {
    return this.lastUpdateStepsCount;
  }

  lastDroppedUpdateSteps() {
    return this.lastDroppedUpdateStepsCount;
  }

  lastUpdateIntervalMilliseconds() {
    return this.lastUpdateIntervalMs;
  }

  updateDeltaMilliseconds() {
    return this.lastUpdateDeltaMs;
  }

  useContinuousUpdates() {
    this.updateMode = "continuous";
    this.accumulatorMs = 0;
    this.restartUpdatePump();
    return 0;
  }

  setFixedUpdateRate(updatesPerSecond) {
    const rate = this.requirePositiveRate(updatesPerSecond, "update rate");
    this.updateMode = "fixed";
    this.fixedUpdatesPerSecond = rate;
    this.stepMs = 1000 / rate;
    this.lastUpdateDeltaMs = this.stepMs;
    this.accumulatorMs = 0;
    this.restartUpdatePump();
    return 0;
  }

  setMaximumRenderRate(framesPerSecond) {
    this.maximumRenderRate = this.requirePositiveRate(framesPerSecond, "render rate");
    return 0;
  }

  useDisplaySynchronizedRendering() {
    this.maximumRenderRate = 0;
    return 0;
  }

  requirePositiveRate(value, name) {
    const rate = Number(value);
    if (!Number.isFinite(rate) || !Number.isInteger(rate) || rate <= 0) throw new Error(`${name} must be a positive integer.`);
    return rate;
  }

  publishDiagnostics(frameIntervalMs, frameWorkMs, updateWorkMs, drawWorkMs, drawHudWorkMs, updateSteps, droppedUpdateSteps = 0) {
    this.lastFrameIntervalMs = frameIntervalMs;
    this.lastFrameWorkMs = frameWorkMs;
    this.lastFrameUpdateWorkMs = updateWorkMs;
    this.lastDrawWorkMs = drawWorkMs;
    this.lastDrawHudWorkMs = drawHudWorkMs;
    this.lastUpdateStepsCount = updateSteps;
    this.lastDroppedUpdateStepsCount = droppedUpdateSteps;
  }

  setDrawSpace(space) {
    this.drawSpace = space === "hud" ? "hud" : "world";
    this.applyCurrentTransform();
  }

  applyCurrentTransform() {
    if (!this.ctx) {
      return;
    }

    if (this.drawSpace === "hud") {
      this.applyHudTransform();
      return;
    }

    this.applyWorldTransform();
  }

  applyWorldTransform() {
    if (!this.ctx) {
      return;
    }

    const scale = this.worldScale * this.devicePixelRatio;
    this.ctx.setTransform(scale, 0, 0, scale, -this.viewLeft * scale, -this.viewTop * scale);
    this.ctx.imageSmoothingEnabled = false;
  }

  applyHudTransform() {
    if (!this.ctx) {
      return;
    }

    const scale = this.devicePixelRatio;
    this.ctx.setTransform(scale, 0, 0, scale, 0, 0);
    this.ctx.imageSmoothingEnabled = false;
  }

  drawPolygonPath(points, finish) {
    if (!this.ctx || points.length < 6 || points.length % 2 !== 0) {
      return 0;
    }

    this.ctx.beginPath();
    this.ctx.moveTo(points[0], points[1]);
    for (let i = 2; i < points.length; i += 2) {
      this.ctx.lineTo(points[i], points[i + 1]);
    }
    this.ctx.closePath();
    finish();
    return 0;
  }

  getImageRecord(source) {
    const normalizedSource = String(source || "");
    let record = this.imageCache.get(normalizedSource);
    if (record) {
      return record;
    }

    const image = new Image();
    record = { image, loaded: false, failed: false };
    image.addEventListener("load", () => {
      record.loaded = true;
    });
    image.addEventListener("error", () => {
      record.failed = true;
    });
    image.src = normalizedSource;
    this.imageCache.set(normalizedSource, record);
    return record;
  }

  canPlaySound() {
    return this.audioUnlocked;
  }

  playSound(source, volume) {
    return this.createAudioHandle(source, volume, false);
  }

  playLoopingSound(source, volume) {
    return this.createAudioHandle(source, volume, true);
  }

  stopSound(handle) {
    const record = this.audioHandles.get(Math.trunc(handle));
    if (!record) {
      return 0;
    }

    record.stopped = true;
    record.pending = false;
    this.pendingAudioHandles.delete(record.handle);
    if (record.audio) {
      try {
        record.audio.pause();
        record.audio.currentTime = 0;
      } catch {
        // Some browsers can reject currentTime changes before metadata loads.
      }
    }
    this.audioStatusChanged?.(record.handle, false);
    return 0;
  }

  setSoundVolume(handle, volume) {
    const record = this.audioHandles.get(Math.trunc(handle));
    if (!record) {
      return 0;
    }

    record.volume = clampUnit(volume);
    if (record.audio) {
      record.audio.volume = record.volume;
    }
    return 0;
  }

  soundIsPlaying(handle) {
    const record = this.audioHandles.get(Math.trunc(handle));
    if (!record || record.stopped || record.failed || record.pending || !record.audio) {
      return false;
    }
    return !record.audio.paused && !record.audio.ended;
  }

  stopAllSounds() {
    for (const handle of Array.from(this.audioHandles.keys())) {
      this.stopSound(handle);
    }
    this.pendingAudioHandles.clear();
    return 0;
  }

  unlockAudio() {
    if (this.audioUnlocked) {
      return;
    }

    this.audioUnlocked = true;
    this.flushPendingAudio();
  }

  flushPendingAudio() {
    const handles = Array.from(this.pendingAudioHandles);
    for (const handle of handles) {
      const record = this.audioHandles.get(handle);
      if (record && record.pending && !record.stopped && !record.failed) {
        this.playAudioRecord(record);
      } else {
        this.pendingAudioHandles.delete(handle);
      }
    }
  }

  createAudioHandle(source, volume, loop) {
    const normalizedSource = String(source || "");
    const handle = this.nextAudioHandle++;
    const record = {
      handle,
      source: normalizedSource,
      audio: null,
      loop,
      volume: clampUnit(volume),
      stopped: false,
      failed: false,
      pending: false
    };

    if (!normalizedSource) {
      record.failed = true;
      console.warn("Audio source is empty.");
    } else if (typeof globalThis.Audio !== "function") {
      record.failed = true;
      console.warn("This browser runtime does not expose HTMLAudioElement playback.");
    } else {
      const audio = new globalThis.Audio(normalizedSource);
      audio.loop = loop;
      audio.volume = record.volume;
      audio.preload = "auto";
      audio.addEventListener("error", () => {
        record.failed = true;
        record.pending = false;
        this.pendingAudioHandles.delete(handle);
        this.audioStatusChanged?.(handle, false);
        console.warn(`Could not load audio '${normalizedSource}'.`);
      });
      audio.addEventListener("ended", () => {
        if (!record.loop) {
          record.stopped = true;
          this.audioStatusChanged?.(handle, false);
        }
      });
      record.audio = audio;
    }

    this.audioHandles.set(handle, record);
    if (!record.failed) {
      if (this.audioUnlocked) {
        this.playAudioRecord(record);
      } else {
        record.pending = true;
        this.pendingAudioHandles.add(handle);
      }
    }

    return handle;
  }

  playAudioRecord(record) {
    if (!record.audio || record.stopped || record.failed) {
      return;
    }

    record.pending = false;
    this.pendingAudioHandles.delete(record.handle);
    try {
      record.audio.currentTime = 0;
    } catch {
      // Some browsers can reject currentTime changes before metadata loads.
    }

    const playResult = record.audio.play();
    if (playResult && typeof playResult.then === "function") {
      playResult.then(() => this.audioStatusChanged?.(record.handle, true)).catch(error => {
        if (record.stopped) {
          return;
        }

        if (error && error.name === "NotAllowedError") {
          record.pending = true;
          this.pendingAudioHandles.add(record.handle);
          this.audioUnlocked = false;
          this.audioStatusChanged?.(record.handle, false);
          return;
        }

        record.failed = true;
        record.pending = false;
        this.pendingAudioHandles.delete(record.handle);
        this.audioStatusChanged?.(record.handle, false);
        const message = error && typeof error.message === "string" ? error.message : String(error);
        console.warn(`Could not play audio '${record.source}': ${message}`);
      });
    } else {
      this.audioStatusChanged?.(record.handle, true);
    }
  }

  runScene(vm, sceneInfo) {
    this.vm = vm;
    this.sceneInfo = sceneInfo;
    this.stop();

    vm.run();

    this.sceneObject = vm.createObject(sceneInfo.typeName);
    this.setDrawSpace("world");
    vm.invokeVoid(sceneInfo.constructor.targetIp, sceneInfo.constructor.frameSize, [this.sceneObject]);
    vm.invokeVoid(sceneInfo.start.targetIp, sceneInfo.start.frameSize, [this.sceneObject]);

    this.running = true;
    this.accumulatorMs = 0;
    this.lastTimestampMs = performance.now();
    this.lastDrawTimestampMs = this.lastTimestampMs;
    this.lastUpdateTimestampMs = 0;
    this.lastUpdatePumpTimestampMs = performance.now();
    this.pendingUpdateWorkMs = 0;
    this.pendingUpdateSteps = 0;
    this.pendingDroppedUpdateSteps = 0;
    this.publishDiagnostics(0, 0, 0, 0, 0, 0);
    this.frameHandle = requestAnimationFrame(this.tick);
    if (this.updateMode === "continuous") this.scheduleUpdatePump();
  }

  stop() {
    this.running = false;
    if (this.frameHandle) {
      cancelAnimationFrame(this.frameHandle);
      this.frameHandle = 0;
    }
    if (this.updateTimerHandle) {
      clearTimeout(this.updateTimerHandle);
      this.updateTimerHandle = 0;
    }
    this.stopAllSounds();
  }

  onFrame(timestamp) {
    this.frameHandle = 0;
    if (!this.running || !this.vm || !this.sceneInfo || !this.sceneObject) {
      return;
    }

    try {
      const schedulerIntervalMs = Math.max(0, timestamp - this.lastTimestampMs);
      this.lastTimestampMs = timestamp;
      if (this.updateMode === "fixed") {
        this.accumulatorMs += Math.min(250, schedulerIntervalMs);
        let steps = 0;
        while (this.accumulatorMs >= this.stepMs && steps < this.maxUpdateStepsPerFrame) {
          this.executeUpdateStep(this.stepMs);
          this.accumulatorMs -= this.stepMs;
          steps += 1;
        }
        if (this.accumulatorMs >= this.stepMs) {
          this.pendingDroppedUpdateSteps += Math.floor(this.accumulatorMs / this.stepMs);
          this.accumulatorMs %= this.stepMs;
        }
      }

      const frameIntervalMs = Math.max(0, timestamp - this.lastDrawTimestampMs);
      if (this.maximumRenderRate > 0 && frameIntervalMs < 1000 / this.maximumRenderRate) {
        this.frameHandle = requestAnimationFrame(this.tick);
        return;
      }
      this.lastDrawTimestampMs = timestamp;

      const frameWorkStartMs = performance.now();
      let drawWorkMs = 0;
      let drawHudWorkMs = 0;

      this.setDrawSpace("world");
      const drawStartMs = performance.now();
      this.vm.invokeVoid(this.sceneInfo.draw.targetIp, this.sceneInfo.draw.frameSize, [this.sceneObject]);
      drawWorkMs = performance.now() - drawStartMs;
      if (this.sceneInfo.drawHud) {
        this.setDrawSpace("hud");
        const drawHudStartMs = performance.now();
        this.vm.invokeVoid(this.sceneInfo.drawHud.targetIp, this.sceneInfo.drawHud.frameSize, [this.sceneObject]);
        drawHudWorkMs = performance.now() - drawHudStartMs;
      }
      this.setDrawSpace("world");
      this.publishDiagnostics(
        frameIntervalMs,
        this.pendingUpdateWorkMs + (performance.now() - frameWorkStartMs),
        this.pendingUpdateWorkMs,
        drawWorkMs,
        drawHudWorkMs,
        this.pendingUpdateSteps,
        this.pendingDroppedUpdateSteps);
      this.pendingUpdateWorkMs = 0;
      this.pendingUpdateSteps = 0;
      this.pendingDroppedUpdateSteps = 0;
      this.frameHandle = requestAnimationFrame(this.tick);
    } catch (error) {
      this.stop();
      this.showFatal(error);
      console.error(error);
    }
  }

  onUpdatePump() {
    this.updateTimerHandle = 0;
    if (!this.running || this.documentHidden || !this.vm || !this.sceneInfo || !this.sceneObject) return;
    try {
      if (this.updateMode !== "continuous") return;
      const now = performance.now();
      const deltaMs = this.lastUpdateTimestampMs > 0 ? Math.min(250, Math.max(0, now - this.lastUpdateTimestampMs)) : 0;
      this.executeUpdateStep(deltaMs);
      this.scheduleUpdatePump();
    } catch (error) {
      this.stop();
      this.showFatal(error);
      console.error(error);
    }
  }

  executeUpdateStep(deltaMs = this.stepMs) {
    const startedAt = performance.now();
    this.lastUpdateIntervalMs = this.lastUpdateTimestampMs > 0 ? startedAt - this.lastUpdateTimestampMs : 0;
    this.lastUpdateTimestampMs = startedAt;
    this.lastUpdateDeltaMs = deltaMs;
    this.beginFixedUpdateStep();
    this.setDrawSpace("world");
    this.vm.invokeVoid(this.sceneInfo.update.targetIp, this.sceneInfo.update.frameSize, [this.sceneObject]);
    const updateWorkMs = performance.now() - startedAt;
    this.lastUpdateWorkMs = updateWorkMs;
    this.pendingUpdateWorkMs += updateWorkMs;
    this.pendingUpdateSteps += 1;
  }

  scheduleUpdatePump() {
    if (!this.running || this.documentHidden) return;
    if (this.updateMode !== "continuous") return;
    this.updateTimerHandle = setTimeout(() => this.onUpdatePump(), 0);
  }

  restartUpdatePump() {
    this.lastUpdatePumpTimestampMs = performance.now();
    if (this.updateTimerHandle) clearTimeout(this.updateTimerHandle);
    this.updateTimerHandle = 0;
    if (this.running && this.updateMode === "continuous") this.scheduleUpdatePump();
  }

  onVisibilityChange() {
    this.documentHidden = typeof document !== "undefined" && document.hidden;
    if (this.workerController) {
      this.workerController.setHidden(this.documentHidden);
      return;
    }
    if (this.documentHidden) {
      if (this.frameHandle) cancelAnimationFrame(this.frameHandle);
      this.frameHandle = 0;
      if (this.updateTimerHandle) clearTimeout(this.updateTimerHandle);
      this.updateTimerHandle = 0;
    } else {
      this.lastTimestampMs = performance.now();
      this.lastDrawTimestampMs = this.lastTimestampMs;
      this.lastUpdateTimestampMs = 0;
      this.restartUpdatePump();
      if (this.running && !this.frameHandle) this.frameHandle = requestAnimationFrame(this.tick);
    }
  }
}

const DrawCommand = Object.freeze({
  Clear: 0, Rectangle: 1, RectangleOutline: 2, Circle: 3, CircleOutline: 4,
  Polygon: 5, PolygonOutline: 6, Line: 7, Text: 8, Image: 9, Sprite: 10, Space: 11
});

export function replayDrawCommands(runtime, numbersBuffer, strings = []) {
  const data = numbersBuffer instanceof Float64Array ? numbersBuffer : new Float64Array(numbersBuffer);
  let cursor = 0;
  while (cursor < data.length) {
    const op = data[cursor++];
    switch (op) {
      case DrawCommand.Clear:
        runtime.clear(data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.Rectangle:
        runtime.drawRectangle(data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.RectangleOutline:
        runtime.drawRectangleOutline(data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.Circle:
        runtime.drawCircle(data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.CircleOutline:
        runtime.drawCircleOutline(data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.Line:
        runtime.drawLine(data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      case DrawCommand.Polygon: {
        const count = data[cursor++];
        const points = Array.from(data.slice(cursor, cursor += count));
        runtime.drawPolygon(points, data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      }
      case DrawCommand.PolygonOutline: {
        const count = data[cursor++];
        const points = Array.from(data.slice(cursor, cursor += count));
        runtime.drawPolygonOutline(points, data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      }
      case DrawCommand.Text: {
        const text = strings[data[cursor++]] ?? "";
        const x = data[cursor++], y = data[cursor++], size = data[cursor++];
        const horizontal = strings[data[cursor++]] ?? "left";
        const vertical = strings[data[cursor++]] ?? "top";
        runtime.drawText(text, x, y, size, horizontal, vertical, data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      }
      case DrawCommand.Image: {
        const source = strings[data[cursor++]] ?? "";
        runtime.drawImage(source, data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      }
      case DrawCommand.Sprite: {
        const source = strings[data[cursor++]] ?? "";
        runtime.drawSprite(source, data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++], data[cursor++]);
        break;
      }
      case DrawCommand.Space: runtime.setDrawSpace(data[cursor++] === 1 ? "hud" : "world"); break;
      default: throw new Error(`Unknown worker draw command ${op}.`);
    }
  }
}

export class WorkerSceneRuntime extends CanvasSceneRuntime {
  constructor(postToMain) {
    super();
    this.postToMain = postToMain;
    this.commandNumbers = [];
    this.commandStrings = [];
    this.workerTimerHandle = 0;
    this.catchUpTurns = 0;
    this.workerAudioPlaying = new Set();
  }

  record(op, ...values) { this.commandNumbers.push(op, ...values); return 0; }
  recordString(value) { this.commandStrings.push(String(value)); return this.commandStrings.length - 1; }
  setDrawSpace(space) { this.drawSpace = space === "hud" ? "hud" : "world"; return this.record(DrawCommand.Space, this.drawSpace === "hud" ? 1 : 0); }
  clear(r, g, b, a) { return this.record(DrawCommand.Clear, r, g, b, a); }
  drawRectangle(...values) { return this.record(DrawCommand.Rectangle, ...values); }
  drawRectangleOutline(...values) { return this.record(DrawCommand.RectangleOutline, ...values); }
  drawCircle(...values) { return this.record(DrawCommand.Circle, ...values); }
  drawCircleOutline(...values) { return this.record(DrawCommand.CircleOutline, ...values); }
  drawLine(...values) { return this.record(DrawCommand.Line, ...values); }
  drawPolygon(points, r, g, b, a) { return this.record(DrawCommand.Polygon, points.length, ...points, r, g, b, a); }
  drawPolygonOutline(points, lineWidth, r, g, b, a) { return this.record(DrawCommand.PolygonOutline, points.length, ...points, lineWidth, r, g, b, a); }
  drawText(text, x, y, size, horizontal, vertical, r, g, b, a) {
    return this.record(DrawCommand.Text, this.recordString(text), x, y, size, this.recordString(horizontal), this.recordString(vertical), r, g, b, a);
  }
  drawImage(source, ...values) { return this.record(DrawCommand.Image, this.recordString(source), ...values); }
  drawSprite(source, ...values) { return this.record(DrawCommand.Sprite, this.recordString(source), ...values); }

  canPlaySound() { return this.audioUnlocked; }
  playSound(source, volume) { return this.workerPlaySound(source, volume, false); }
  playLoopingSound(source, volume) { return this.workerPlaySound(source, volume, true); }
  workerPlaySound(source, volume, loop) {
    const handle = this.nextAudioHandle++;
    this.postToMain({ type: "audio", action: "play", handle, source: String(source), volume, loop });
    return handle;
  }
  stopSound(handle) { handle = Math.trunc(handle); this.workerAudioPlaying.delete(handle); this.postToMain({ type: "audio", action: "stop", handle }); return 0; }
  setSoundVolume(handle, volume) { this.postToMain({ type: "audio", action: "volume", handle: Math.trunc(handle), volume }); return 0; }
  soundIsPlaying(handle) { return this.workerAudioPlaying.has(Math.trunc(handle)); }
  applyAudioStatus(handle, playing) {
    handle = Math.trunc(handle);
    if (playing) this.workerAudioPlaying.add(handle);
    else this.workerAudioPlaying.delete(handle);
  }
  stopAllSounds() { this.workerAudioPlaying.clear(); this.postToMain({ type: "audio", action: "stopAll" }); return 0; }
  setMaximumRenderRate(rate) { const result = super.setMaximumRenderRate(rate); this.postToMain({ type: "config", maximumRenderRate: this.maximumRenderRate }); return result; }
  useDisplaySynchronizedRendering() { const result = super.useDisplaySynchronizedRendering(); this.postToMain({ type: "config", maximumRenderRate: 0 }); return result; }

  applyViewport(viewport) {
    if (!viewport) return;
    for (const key of ["safeLeft", "safeTop", "safeWidth", "safeHeight", "viewLeft", "viewTop", "viewWidth", "viewHeight", "viewportWidth", "viewportHeight", "worldScale"])
      if (Number.isFinite(viewport[key])) this[key] = viewport[key];
  }

  applyInput(input) {
    if (!input) return;
    this.keysDown = new Set(input.keysDown ?? []);
    this.pointerScreenX = input.pointerScreenX ?? this.pointerScreenX;
    this.pointerScreenY = input.pointerScreenY ?? this.pointerScreenY;
    this.pointerIsDownNow = input.pointerIsDownNow === true;
    this.pointerPressedPending ||= input.pointerPressed === true;
    this.pointerReleasedPending ||= input.pointerReleased === true;
    this.audioUnlocked = input.audioUnlocked === true;
  }

  initializeWorkerScene(vm, sceneInfo) {
    this.vm = vm;
    this.sceneInfo = sceneInfo;
    vm.run();
    this.sceneObject = vm.createObject(sceneInfo.typeName);
    vm.invokeVoid(sceneInfo.constructor.targetIp, sceneInfo.constructor.frameSize, [this.sceneObject]);
    vm.invokeVoid(sceneInfo.start.targetIp, sceneInfo.start.frameSize, [this.sceneObject]);
    this.running = true;
    this.accumulatorMs = 0;
    this.lastTimestampMs = performance.now();
    this.lastDrawTimestampMs = this.lastTimestampMs;
    this.lastUpdatePumpTimestampMs = this.lastTimestampMs;
    this.scheduleWorkerPump();
  }

  restartUpdatePump() {
    this.lastUpdatePumpTimestampMs = performance.now();
    if (this.workerTimerHandle) clearTimeout(this.workerTimerHandle);
    this.workerTimerHandle = 0;
    if (this.running) this.scheduleWorkerPump();
  }

  scheduleWorkerPump(delay = 0) {
    if (!this.running || this.documentHidden || this.workerTimerHandle) return;
    this.workerTimerHandle = setTimeout(() => this.workerPump(), delay);
  }

  setWorkerHidden(hidden) {
    this.documentHidden = hidden === true;
    if (this.documentHidden) {
      if (this.workerTimerHandle) clearTimeout(this.workerTimerHandle);
      this.workerTimerHandle = 0;
      return;
    }
    this.lastUpdateTimestampMs = 0;
    this.lastUpdatePumpTimestampMs = performance.now();
    this.accumulatorMs = 0;
    this.scheduleWorkerPump();
  }

  workerPump() {
    this.workerTimerHandle = 0;
    if (!this.running || this.documentHidden) return;
    try {
      const now = performance.now();
      if (this.updateMode === "continuous") {
        const delta = this.lastUpdateTimestampMs > 0 ? Math.min(250, now - this.lastUpdateTimestampMs) : 0;
        this.executeUpdateStep(delta);
        this.scheduleWorkerPump(0);
        return;
      }
      this.accumulatorMs += Math.min(250, Math.max(0, now - this.lastUpdatePumpTimestampMs));
      this.lastUpdatePumpTimestampMs = now;
      if (this.accumulatorMs >= this.stepMs) {
        this.executeUpdateStep(this.stepMs);
        this.accumulatorMs -= this.stepMs;
        this.catchUpTurns += 1;
        if (this.accumulatorMs >= this.stepMs && this.catchUpTurns >= this.maxUpdateStepsPerFrame) {
          this.pendingDroppedUpdateSteps += Math.floor(this.accumulatorMs / this.stepMs);
          this.accumulatorMs %= this.stepMs;
          this.catchUpTurns = 0;
        }
      } else {
        this.catchUpTurns = 0;
      }
      this.scheduleWorkerPump(this.accumulatorMs >= this.stepMs ? 0 : Math.max(0, this.stepMs - this.accumulatorMs));
    } catch (error) {
      this.running = false;
      this.postToMain({ type: "fatal", error: serializeWorkerError(error) });
    }
  }

  renderWorkerFrame(id, timestamp, viewport, input) {
    this.applyViewport(viewport);
    this.applyInput(input);
    const frameIntervalMs = Math.max(0, timestamp - this.lastDrawTimestampMs);
    this.lastDrawTimestampMs = timestamp;
    this.commandNumbers = [];
    this.commandStrings = [];
    const started = performance.now();
    this.setDrawSpace("world");
    const drawStarted = performance.now();
    this.vm.invokeVoid(this.sceneInfo.draw.targetIp, this.sceneInfo.draw.frameSize, [this.sceneObject]);
    const drawWork = performance.now() - drawStarted;
    let hudWork = 0;
    if (this.sceneInfo.drawHud) {
      this.setDrawSpace("hud");
      const hudStarted = performance.now();
      this.vm.invokeVoid(this.sceneInfo.drawHud.targetIp, this.sceneInfo.drawHud.frameSize, [this.sceneObject]);
      hudWork = performance.now() - hudStarted;
    }
    const frameWorkMs = this.pendingUpdateWorkMs + (performance.now() - started);
    const frameUpdateWorkMs = this.pendingUpdateWorkMs;
    const frameUpdateSteps = this.pendingUpdateSteps;
    const frameDroppedUpdateSteps = this.pendingDroppedUpdateSteps;
    const diagnostics = {
      frameIntervalMs, frameWorkMs,
      updateWorkMs: this.lastUpdateWorkMs, drawWorkMs: drawWork, drawHudWorkMs: hudWork,
      updateSteps: frameUpdateSteps, droppedUpdateSteps: frameDroppedUpdateSteps,
      updateIntervalMs: this.lastUpdateIntervalMs, updateDeltaMs: this.lastUpdateDeltaMs
    };
    this.publishDiagnostics(
      frameIntervalMs,
      frameWorkMs,
      frameUpdateWorkMs,
      drawWork,
      hudWork,
      frameUpdateSteps,
      frameDroppedUpdateSteps);
    this.pendingUpdateWorkMs = 0; this.pendingUpdateSteps = 0; this.pendingDroppedUpdateSteps = 0;
    const commands = new Float64Array(this.commandNumbers);
    this.postToMain({ type: "frame", id, commands: commands.buffer, strings: this.commandStrings, diagnostics }, [commands.buffer]);
  }
}

function serializeWorkerError(error) {
  return { name: error?.name ?? "Error", message: error?.message ?? String(error), stack: error?.stack ?? "", payload: error?.payload ?? null };
}

export class WebVm {
  constructor(bytecodeBytes, options = {}) {
    this.bytes = bytecodeBytes;
    const header = readHeader(this.bytes);
    this.codeEnd = header.codeEnd;
    this.debugMap = header.debugMap;
    this.view = header.view;
    this.metadata = header.metadata;
    this.instructions = decodeInstructions(this.bytes, this.view, this.codeEnd, this.metadata);
    this.decodedInstructionCount = this.instructions.reduce((count, instruction) => count + (instruction ? 1 : 0), 0);
    this.ip = HEADER_SIZE;

    this.stack = [];
    this.localsStack = new Array(Math.max(8, options.initialLocals || 8)).fill(0);
    this.frameBase = 0;
    this.frameSize = this.localsStack.length;
    this.allowFrameGrowth = true;
    this.localsTop = this.frameSize;
    this.localsHighWater = this.localsTop;
    this.hostArgumentBuffers = new Map();
    this.globals = new Array(8).fill(0);
    this.callStack = [];
    this.callFramePool = [];
    this.nextWindowHandle = 1;

    this.output = typeof options.output === "function" ? options.output : line => console.log(line);
    this.hostTarget = "vm-web";
    this.monoOriginMs = performance.now();
    this.hostBindings = new Map();
    this.sceneHost = options.sceneHost ?? null;
    this.functionNames = this.metadata.functionNames;
    this.profiler = new RuntimeProfiler(this, options.profileEnabled === true);
    if (this.profiler.enabled) this.profiler.start();

    this.initializeHostBindings();
  }

  initializeHostBindings() {
    const printBinding = {
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
    };
    this.hostBindings.set("standard.input_output.print", printBinding);
    this.hostBindings.set("std.io.print", printBinding);

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

    this.hostBindings.set("std.math.minimum", {
      arity: 2,
      handler: args => Math.min(
        toNumber(args[0], message => this.throwRuntime(message)),
        toNumber(args[1], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.maximum", {
      arity: 2,
      handler: args => Math.max(
        toNumber(args[0], message => this.throwRuntime(message)),
        toNumber(args[1], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.absolute", {
      arity: 1,
      handler: args => Math.abs(
        toNumber(args[0], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.sign", {
      arity: 1,
      handler: args => Math.sign(
        toNumber(args[0], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.lerp", {
      arity: 3,
      handler: args => {
        const start = toNumber(args[0], message => this.throwRuntime(message));
        const end = toNumber(args[1], message => this.throwRuntime(message));
        const amount = toNumber(args[2], message => this.throwRuntime(message));
        return start + ((end - start) * amount);
      }
    });

    this.hostBindings.set("std.math.sine", {
      arity: 1,
      handler: args => Math.sin(
        toNumber(args[0], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.cosine", {
      arity: 1,
      handler: args => Math.cos(
        toNumber(args[0], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.square_root", {
      arity: 1,
      handler: args => Math.sqrt(
        toNumber(args[0], message => this.throwRuntime(message))
      )
    });

    this.hostBindings.set("std.math.random", {
      arity: 0,
      handler: () => Math.random()
    });

    this.registerUnsupportedBinding("standard.input_output.read_line", 0, "native-only API");
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
    this.hostBindings.set("engine.input.key_down_scene", {
      arity: 1,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.keyDown(toNumber(args[0], message => this.throwRuntime(message))) ? 1 : 0;
      }
    });
    this.hostBindings.set("engine.input.pointer_world_x_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.pointerWorldX() : 0
    });
    this.hostBindings.set("engine.input.pointer_world_y_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.pointerWorldY() : 0
    });
    this.hostBindings.set("engine.input.pointer_screen_x_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.pointerScreenXPosition() : 0
    });
    this.hostBindings.set("engine.input.pointer_screen_y_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.pointerScreenYPosition() : 0
    });
    this.hostBindings.set("engine.input.pointer_is_down_scene", {
      arity: 0,
      handler: () => this.sceneHost && this.sceneHost.pointerIsDown() ? 1 : 0
    });
    this.hostBindings.set("engine.input.pointer_was_pressed_scene", {
      arity: 0,
      handler: () => this.sceneHost && this.sceneHost.pointerWasPressed() ? 1 : 0
    });
    this.hostBindings.set("engine.input.pointer_was_released_scene", {
      arity: 0,
      handler: () => this.sceneHost && this.sceneHost.pointerWasReleased() ? 1 : 0
    });
    this.hostBindings.set("engine.window.camera_view_left_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewLeft() : 0
    });
    this.hostBindings.set("engine.window.camera_view_top_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewTop() : 0
    });
    this.hostBindings.set("engine.window.camera_view_width_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewWidth() : 640
    });
    this.hostBindings.set("engine.window.camera_view_height_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewHeight() : 360
    });
    this.hostBindings.set("engine.window.camera_view_right_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewRight() : 640
    });
    this.hostBindings.set("engine.window.camera_view_bottom_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraViewBottom() : 360
    });
    this.hostBindings.set("engine.window.camera_safe_left_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeLeft() : 0
    });
    this.hostBindings.set("engine.window.camera_safe_top_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeTop() : 0
    });
    this.hostBindings.set("engine.window.camera_safe_width_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeWidth() : 640
    });
    this.hostBindings.set("engine.window.camera_safe_height_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeHeight() : 360
    });
    this.hostBindings.set("engine.window.camera_safe_right_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeRight() : 640
    });
    this.hostBindings.set("engine.window.camera_safe_bottom_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.cameraSafeBottom() : 360
    });
    this.hostBindings.set("engine.window.screen_width_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.screenWidth() : 640
    });
    this.hostBindings.set("engine.window.screen_height_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.screenHeight() : 360
    });
    this.hostBindings.set("engine.diagnostics.last_frame_interval_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastFrameIntervalMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.estimated_frames_per_second_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.estimatedFramesPerSecond() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_frame_work_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastFrameWorkMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_update_work_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastUpdateWorkMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_draw_work_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastDrawWorkMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_draw_hud_work_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastDrawHudWorkMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_update_steps_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastUpdateSteps() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_dropped_update_steps_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastDroppedUpdateSteps() : 0
    });
    this.hostBindings.set("engine.diagnostics.last_update_interval_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.lastUpdateIntervalMilliseconds() : 0
    });
    this.hostBindings.set("engine.diagnostics.update_delta_milliseconds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.updateDeltaMilliseconds() : 0
    });
    this.hostBindings.set("engine.runtime.use_continuous_updates_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.useContinuousUpdates() : 0
    });
    this.hostBindings.set("engine.runtime.set_fixed_update_rate_scene", {
      arity: 1,
      handler: args => {
        try { return this.sceneHost ? this.sceneHost.setFixedUpdateRate(args[0]) : 0; }
        catch (error) { this.throwRuntime(error.message, "HostBindingError"); }
      }
    });
    this.hostBindings.set("engine.runtime.set_maximum_render_rate_scene", {
      arity: 1,
      handler: args => {
        try { return this.sceneHost ? this.sceneHost.setMaximumRenderRate(args[0]) : 0; }
        catch (error) { this.throwRuntime(error.message, "HostBindingError"); }
      }
    });
    this.hostBindings.set("engine.runtime.use_display_synchronized_rendering_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.useDisplaySynchronizedRendering() : 0
    });
    this.hostBindings.set("engine.audio.can_play_sound_scene", {
      arity: 0,
      handler: () => this.sceneHost && this.sceneHost.canPlaySound() ? 1 : 0
    });
    this.hostBindings.set("engine.audio.play_sound_scene", {
      arity: 2,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.playSound(
          toText(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.audio.play_looping_sound_scene", {
      arity: 2,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.playLoopingSound(
          toText(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.audio.stop_sound_scene", {
      arity: 1,
      handler: args => this.sceneHost
        ? this.sceneHost.stopSound(toNumber(args[0], message => this.throwRuntime(message)))
        : 0
    });
    this.hostBindings.set("engine.audio.set_sound_volume_scene", {
      arity: 2,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.setSoundVolume(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.audio.sound_is_playing_scene", {
      arity: 1,
      handler: args => this.sceneHost && this.sceneHost.soundIsPlaying(toNumber(args[0], message => this.throwRuntime(message))) ? 1 : 0
    });
    this.hostBindings.set("engine.audio.stop_all_sounds_scene", {
      arity: 0,
      handler: () => this.sceneHost ? this.sceneHost.stopAllSounds() : 0
    });
    this.hostBindings.set("engine.gfx.clear", {
      arity: 5,
      handler: () => 0
    });
    this.hostBindings.set("engine.gfx.clear_scene", {
      arity: 4,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.clear(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_rect", {
      arity: 9,
      handler: () => 0
    });
    const drawRectangleSceneBinding = {
      arity: 8,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawRectangle(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message))
        );
      }
    };
    this.hostBindings.set("engine.gfx.draw_rect_scene", drawRectangleSceneBinding);
    this.hostBindings.set("engine.gfx.draw_rectangle_scene", drawRectangleSceneBinding);
    this.hostBindings.set("engine.gfx.draw_rectangle_outline_scene", {
      arity: 9,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawRectangleOutline(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message)),
          toNumber(args[8], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_circle_scene", {
      arity: 7,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawCircle(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_circle_outline_scene", {
      arity: 8,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawCircleOutline(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_polygon_scene", {
      arity: 5,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawPolygon(
          toNumberArray(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_polygon_outline_scene", {
      arity: 6,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawPolygonOutline(
          toNumberArray(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_line_scene", {
      arity: 8,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawLine(
          toNumber(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_text_scene", {
      arity: 10,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawText(
          toText(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toText(args[4], message => this.throwRuntime(message)),
          toText(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message)),
          toNumber(args[8], message => this.throwRuntime(message)),
          toNumber(args[9], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_image_scene", {
      arity: 6,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawImage(
          toText(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message))
        );
      }
    });
    this.hostBindings.set("engine.gfx.draw_sprite_scene", {
      arity: 10,
      handler: args => {
        if (!this.sceneHost) {
          return 0;
        }
        return this.sceneHost.drawSprite(
          toText(args[0], message => this.throwRuntime(message)),
          toNumber(args[1], message => this.throwRuntime(message)),
          toNumber(args[2], message => this.throwRuntime(message)),
          toNumber(args[3], message => this.throwRuntime(message)),
          toNumber(args[4], message => this.throwRuntime(message)),
          toNumber(args[5], message => this.throwRuntime(message)),
          toNumber(args[6], message => this.throwRuntime(message)),
          toNumber(args[7], message => this.throwRuntime(message)),
          toNumber(args[8], message => this.throwRuntime(message)),
          toNumber(args[9], message => this.throwRuntime(message))
        );
      }
    });
  }

  createObject(typeIdOrName) {
    const typeId = typeof typeIdOrName === "number"
      ? typeIdOrName
      : this.metadata.types.findIndex(type => type.name === typeIdOrName);
    if (typeId < 0 || typeId >= this.metadata.types.length) this.throwRuntime(`Unknown object type '${typeIdOrName}'`);
    return createVmObject(typeId, this.metadata.types[typeId], this.metadata.fields.length);
  }

  createRecord(typeIdOrName) {
    const typeId = typeof typeIdOrName === "number"
      ? typeIdOrName
      : this.metadata.types.findIndex(type => type.name === typeIdOrName);
    if (typeId < 0 || typeId >= this.metadata.types.length) this.throwRuntime(`Unknown record type '${typeIdOrName}'`);
    return createVmObject(typeId, this.metadata.types[typeId], this.metadata.fields.length);
  }

  invokeVoid(targetIp, localCount, args = []) {
    const savedIp = this.ip;
    const savedFrameBase = this.frameBase;
    const savedFrameSize = this.frameSize;
    const savedAllowFrameGrowth = this.allowFrameGrowth;
    const savedLocalsTop = this.localsTop;
    const savedStackDepth = this.stack.length;
    const savedCallStackDepth = this.callStack.length;
    const savedProfileDepth = this.profiler.functionFrames.length;

    const frameSize = Math.max(localCount || 0, args.length, 1);
    const frameBase = this.localsTop;
    this.ensureLocalsCapacity(frameBase + frameSize);
    if (frameSize > args.length) this.localsStack.fill(0, frameBase + args.length, frameBase + frameSize);
    if (this.profiler.enabled) this.profiler.allocate("callFrames");
    for (let i = 0; i < args.length; i += 1) {
      this.localsStack[frameBase + i] = args[i];
    }

    this.frameBase = frameBase;
    this.frameSize = frameSize;
    this.allowFrameGrowth = false;
    this.localsTop = frameBase + frameSize;
    this.ip = targetIp;
    if (this.profiler.enabled) this.profiler.enterFunction(targetIp);

    try {
      this.run();
    } finally {
      while (this.profiler.functionFrames.length > savedProfileDepth) {
        this.profiler.leaveFunction();
      }
      this.frameBase = savedFrameBase;
      this.frameSize = savedFrameSize;
      this.allowFrameGrowth = savedAllowFrameGrowth;
      this.localsTop = savedLocalsTop;
      this.ip = savedIp;
      this.stack.length = savedStackDepth;
      this.callStack.length = savedCallStackDepth;
    }
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

      const instruction = this.instructions[this.ip];
      if (!instruction) this.throwRuntime(`Invalid instruction address ${this.ip}`);
      const op = instruction.op;
      const instructionIp = instruction.byteIp;
      this.currentInstructionIp = instructionIp;
      this.ip = instruction.nextIp;
      if (this.profiler.enabled) this.profiler.instruction(op);

      switch (op) {
        case OpCode.PushConst:
          this.stack.push(instruction.a);
          break;

        case OpCode.PushReal:
          this.stack.push(instruction.a);
          break;

        case OpCode.PushWideInteger:
          this.stack.push(instruction.a);
          break;

        case OpCode.PushString: {
          this.stack.push(this.metadata.strings[instruction.a]);
          break;
        }

        case OpCode.Add: {
          this.ensureStack(2);
          const right = this.stack.pop();
          const left = this.stack.pop();
          if (typeof left === "string" || typeof right === "string") {
            this.stack.push(String(left) + String(right));
          } else {
            if (!isNumberValue(left) || !Number.isFinite(left) || !isNumberValue(right) || !Number.isFinite(right)) this.throwRuntime("Expected number on stack");
            this.stack.push(left + right);
          }
          break;
        }

        case OpCode.Sub: {
          const b = this.popNumber(); const a = this.popNumber(); this.stack.push(a - b);
          break;
        }

        case OpCode.Mul: {
          const b = this.popNumber(); const a = this.popNumber(); this.stack.push(a * b);
          break;
        }

        case OpCode.Div: {
          const b = this.popNumber(); const a = this.popNumber();
          if (b === 0) this.throwRuntime("Division by zero in bytecode.");
          this.stack.push(a / b);
          break;
        }

        case OpCode.IntDiv: {
          const b = Math.trunc(this.popNumber()); const a = Math.trunc(this.popNumber());
          if (b === 0) this.throwRuntime("Division by zero in bytecode.");
          this.stack.push(Math.trunc(a / b));
          break;
        }

        case OpCode.Mod: {
          const b = this.popNumber(); const a = this.popNumber();
          if (b === 0) this.throwRuntime("Modulo by zero in bytecode.");
          this.stack.push(a % b);
          break;
        }

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
          this.ip = instruction.a;
          break;

        case OpCode.JumpIfZero: {
          const test = this.popNumber();
          const target = instruction.a;
          if (test === 0) {
            this.ip = target;
          }
          break;
        }

        case OpCode.JumpIfNotZero: {
          const test = this.popNumber();
          const target = instruction.a;
          if (test !== 0) {
            this.ip = target;
          }
          break;
        }

        case OpCode.Load: {
          const slot = instruction.a;
          if (slot < 0) this.throwRuntime(`Negative local index ${slot}`);
          if (slot >= this.frameSize) {
            if (this.allowFrameGrowth) this.ensureLocals(slot);
            else this.throwRuntime(`Local index ${slot} is outside frame size ${this.frameSize}`);
          }
          this.stack.push(this.localsStack[this.frameBase + slot]);
          break;
        }

        case OpCode.Store: {
          const slot = instruction.a;
          this.ensureStack(1);
          if (slot < 0) this.throwRuntime(`Negative local index ${slot}`);
          if (slot >= this.frameSize) {
            if (this.allowFrameGrowth) this.ensureLocals(slot);
            else this.throwRuntime(`Local index ${slot} is outside frame size ${this.frameSize}`);
          }
          this.localsStack[this.frameBase + slot] = this.stack.pop();
          break;
        }

        case OpCode.LoadGlobal: {
          const slot = instruction.a;
          if (slot < 0) this.throwRuntime(`Negative global index ${slot}`);
          while (slot >= this.globals.length) this.globals.push(0);
          this.stack.push(this.globals[slot]);
          break;
        }

        case OpCode.StoreGlobal: {
          const slot = instruction.a;
          this.ensureStack(1);
          if (slot < 0) this.throwRuntime(`Negative global index ${slot}`);
          while (slot >= this.globals.length) this.globals.push(0);
          this.globals[slot] = this.stack.pop();
          break;
        }

        case OpCode.Eq: {
          this.ensureStack(2);
          const right = this.stack.pop();
          const left = this.stack.pop();
          this.stack.push(valueEquals(left, right) ? 1 : 0);
          break;
        }

        case OpCode.Lt: {
          const b = this.popNumber(); const a = this.popNumber(); this.stack.push(a < b ? 1 : 0);
          break;
        }

        case OpCode.Gt: {
          const b = this.popNumber(); const a = this.popNumber(); this.stack.push(a > b ? 1 : 0);
          break;
        }

        case OpCode.Call: {
          const callIp = instructionIp;
          const target = instruction.a;
          const argCount = instruction.b;
          const localCount = instruction.c;
          const newFrameSize = Math.max(localCount, argCount);
          const newFrameBase = this.localsTop;
          this.ensureLocalsCapacity(newFrameBase + newFrameSize);
          if (newFrameSize > argCount) this.localsStack.fill(0, newFrameBase + argCount, newFrameBase + newFrameSize);
          if (this.profiler.enabled) this.profiler.allocate("callFrames");
          for (let i = argCount - 1; i >= 0; i -= 1) {
            this.ensureStack(1);
            this.localsStack[newFrameBase + i] = this.stack.pop();
          }
          const callDepth = this.callStack.length;
          const callFrame = this.callFramePool[callDepth] ?? (this.callFramePool[callDepth] = {});
          callFrame.returnIp = this.ip;
          callFrame.callIp = callIp;
          callFrame.frameBase = this.frameBase;
          callFrame.frameSize = this.frameSize;
          callFrame.localsTop = this.localsTop;
          callFrame.allowFrameGrowth = this.allowFrameGrowth;
          this.callStack.push(callFrame);
          this.frameBase = newFrameBase;
          this.frameSize = newFrameSize;
          this.allowFrameGrowth = false;
          this.localsTop = newFrameBase + newFrameSize;
          this.ip = target;
          if (this.profiler.enabled) this.profiler.enterFunction(target);
          break;
        }

        case OpCode.Ret: {
          this.ensureStack(1);
          const retVal = this.stack.pop();
          if (this.profiler.enabled) this.profiler.leaveFunction();
          if (this.callStack.length === 0) {
            return;
          }
          const frame = this.callStack.pop();
          this.frameBase = frame.frameBase;
          this.frameSize = frame.frameSize;
          this.allowFrameGrowth = frame.allowFrameGrowth;
          this.localsTop = frame.localsTop;
          this.ip = frame.returnIp;
          this.stack.push(retVal);
          break;
        }

        case OpCode.NewArray: {
          const count = instruction.a;
          this.ensureStack(count);
          const items = new Array(count);
          if (this.profiler.enabled) this.profiler.allocate("arrays");
          for (let i = count - 1; i >= 0; i -= 1) {
            items[i] = this.stack.pop();
          }
          this.stack.push(items);
          break;
        }

        case OpCode.ArrayLength: {
          this.ensureStack(1);
          const collection = this.stack.pop();
          const length = tryGetCollectionLength(collection);
          if (length === null) {
            this.throwRuntime("ArrayLength expects array, map, set, queue, or stack");
          }
          this.stack.push(length);
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

        case OpCode.ArrayAppend: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const arr = this.stack.pop();
          if (!Array.isArray(arr)) {
            this.throwRuntime("ArrayAppend expects array");
          }
          arr.push(value);
          this.stack.push(0);
          break;
        }

        case OpCode.ArrayRemoveAt: {
          this.ensureStack(2);
          const index = Math.trunc(this.popNumber());
          const arr = this.stack.pop();
          if (!Array.isArray(arr)) {
            this.throwRuntime("ArrayRemoveAt expects array");
          }
          if (index < 0 || index >= arr.length) {
            this.throwRuntime("Array index out of range");
          }
          arr.splice(index, 1);
          this.stack.push(0);
          break;
        }

        case OpCode.NewMap:
          this.stack.push(createVmMap());
          break;

        case OpCode.MapGet: {
          this.ensureStack(2);
          const key = this.stack.pop();
          const map = this.stack.pop();
          if (!isVmMap(map)) {
            this.throwRuntime("MapGet expects map");
          }
          const result = vmMapTryGet(map, key);
          if (!result.found) {
            this.throwRuntime("Map key not found");
          }
          this.stack.push(result.value);
          break;
        }

        case OpCode.MapSet: {
          this.ensureStack(3);
          const value = this.stack.pop();
          const key = this.stack.pop();
          const map = this.stack.pop();
          if (!isVmMap(map)) {
            this.throwRuntime("MapSet expects map");
          }
          vmMapSet(map, key, value);
          this.stack.push(value);
          break;
        }

        case OpCode.MapContains: {
          this.ensureStack(2);
          const key = this.stack.pop();
          const map = this.stack.pop();
          if (!isVmMap(map)) {
            this.throwRuntime("MapContains expects map");
          }
          this.stack.push(vmMapContains(map, key) ? 1 : 0);
          break;
        }

        case OpCode.MapRemove: {
          this.ensureStack(2);
          const key = this.stack.pop();
          const map = this.stack.pop();
          if (!isVmMap(map)) {
            this.throwRuntime("MapRemove expects map");
          }
          vmMapRemove(map, key);
          this.stack.push(0);
          break;
        }

        case OpCode.NewSet:
          this.stack.push(createVmSet());
          break;

        case OpCode.SetAdd: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const set = this.stack.pop();
          if (!isVmSet(set)) {
            this.throwRuntime("SetAdd expects set");
          }
          vmSetAdd(set, value);
          this.stack.push(0);
          break;
        }

        case OpCode.SetContains: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const set = this.stack.pop();
          if (!isVmSet(set)) {
            this.throwRuntime("SetContains expects set");
          }
          this.stack.push(vmSetContains(set, value) ? 1 : 0);
          break;
        }

        case OpCode.SetRemove: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const set = this.stack.pop();
          if (!isVmSet(set)) {
            this.throwRuntime("SetRemove expects set");
          }
          vmSetRemove(set, value);
          this.stack.push(0);
          break;
        }

        case OpCode.NewQueue:
          this.stack.push({ __vmQueue: true, items: [] });
          break;

        case OpCode.QueueEnqueue: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const queue = this.stack.pop();
          if (!isVmQueue(queue)) {
            this.throwRuntime("QueueEnqueue expects queue");
          }
          queue.items.push(value);
          this.stack.push(0);
          break;
        }

        case OpCode.QueueDequeue: {
          this.ensureStack(1);
          const queue = this.stack.pop();
          if (!isVmQueue(queue)) {
            this.throwRuntime("QueueDequeue expects queue");
          }
          if (queue.items.length === 0) {
            this.throwRuntime("Queue is empty");
          }
          this.stack.push(queue.items.shift());
          break;
        }

        case OpCode.QueuePeek: {
          this.ensureStack(1);
          const queue = this.stack.pop();
          if (!isVmQueue(queue)) {
            this.throwRuntime("QueuePeek expects queue");
          }
          if (queue.items.length === 0) {
            this.throwRuntime("Queue is empty");
          }
          this.stack.push(queue.items[0]);
          break;
        }

        case OpCode.NewStack:
          this.stack.push({ __vmStack: true, items: [] });
          break;

        case OpCode.StackPush: {
          this.ensureStack(2);
          const value = this.stack.pop();
          const stack = this.stack.pop();
          if (!isVmStack(stack)) {
            this.throwRuntime("StackPush expects stack");
          }
          stack.items.push(value);
          this.stack.push(0);
          break;
        }

        case OpCode.StackPop: {
          this.ensureStack(1);
          const stack = this.stack.pop();
          if (!isVmStack(stack)) {
            this.throwRuntime("StackPop expects stack");
          }
          if (stack.items.length === 0) {
            this.throwRuntime("Stack is empty");
          }
          this.stack.push(stack.items.pop());
          break;
        }

        case OpCode.StackPeek: {
          this.ensureStack(1);
          const stack = this.stack.pop();
          if (!isVmStack(stack)) {
            this.throwRuntime("StackPeek expects stack");
          }
          if (stack.items.length === 0) {
            this.throwRuntime("Stack is empty");
          }
          this.stack.push(stack.items[stack.items.length - 1]);
          break;
        }

        case OpCode.NewArrayN: {
          this.ensureStack(1);
          const size = Math.trunc(this.popNumber());
          if (size < 0) {
            this.throwRuntime("Array size must be non-negative");
          }
          if (this.profiler.enabled) this.profiler.allocate("arrays");
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

        case OpCode.FallibleSuccess: {
          this.ensureStack(1);
          this.stack.push(createFallibleSuccess(this.stack.pop()));
          break;
        }

        case OpCode.FallibleError: {
          this.ensureStack(2);
          const message = String(this.stack.pop() ?? "");
          const code = this.stack.pop();
          this.stack.push(createFallibleError(code, message));
          break;
        }

        case OpCode.FallibleIsError: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (!isVmFallible(value)) {
            this.throwRuntime("FallibleIsError expects fallible value");
          }
          this.stack.push(value.isError ? 1 : 0);
          break;
        }

        case OpCode.FallibleValue: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (!isVmFallible(value)) {
            this.throwRuntime("FallibleValue expects fallible value");
          }
          if (value.isError) {
            this.throwRuntime("Cannot unwrap failed fallible value without handling");
          }
          this.stack.push(value.value ?? 0);
          break;
        }

        case OpCode.FallibleErrorCode: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (!isVmFallible(value)) {
            this.throwRuntime("FallibleErrorCode expects fallible value");
          }
          if (!value.isError) {
            this.throwRuntime("Cannot read error code from successful fallible value");
          }
          this.stack.push(value.code ?? 0);
          break;
        }

        case OpCode.FallibleErrorMessage: {
          this.ensureStack(1);
          const value = this.stack.pop();
          if (!isVmFallible(value)) {
            this.throwRuntime("FallibleErrorMessage expects fallible value");
          }
          if (!value.isError) {
            this.throwRuntime("Cannot read error message from successful fallible value");
          }
          this.stack.push(value.message);
          break;
        }

        case OpCode.CastInteger:
          this.stack.push(this.coerceNumericCastToInteger(true, "integer"));
          break;

        case OpCode.CastWhole:
          this.stack.push(this.coerceNumericCastToInteger(false, "whole"));
          break;

        case OpCode.CastReal:
          this.stack.push(this.popNumber());
          break;

        case OpCode.CheckedSizedNumericCast:
          this.stack.push(this.coerceCheckedSizedNumeric(instruction.a));
          break;

        case OpCode.NewObject: {
          const typeId = instruction.a;
          if (this.profiler.enabled) this.profiler.allocate("objects");
          this.stack.push(this.createObject(typeId));
          break;
        }

        case OpCode.NewRecord: {
          const typeId = instruction.a;
          if (this.profiler.enabled) this.profiler.allocate("objects");
          this.stack.push(this.createRecord(typeId));
          break;
        }

        case OpCode.GetField: {
          const slot = instruction.a;
          const fieldName = this.metadata.fields[slot];
          this.ensureStack(1);
          const target = this.stack.pop();
          if (!isVmObject(target)) {
            this.throwRuntime("GetField expects object");
          }
          if (!target.initializedFields[slot]) {
            this.throwRuntime(`Field '${fieldName}' is not initialized on object '${target.typeName}'`);
          }
          this.stack.push(target.fields[slot]);
          break;
        }

        case OpCode.SetField: {
          const slot = instruction.a;
          this.ensureStack(2);
          const value = this.stack.pop();
          const target = this.stack.pop();
          if (!isVmObject(target)) {
            this.throwRuntime("SetField expects object");
          }
          target.fields[slot] = value;
          target.initializedFields[slot] = 1;
          this.stack.push(value);
          break;
        }

        case OpCode.GetTypeName: {
          this.ensureStack(1);
          const target = this.stack.pop();
          if (!isVmObject(target)) {
            this.throwRuntime("GetTypeName expects object");
          }
          this.stack.push(target.typeName);
          break;
        }

        case OpCode.InterfaceCall: {
          const callIp = instructionIp;
          const dispatch = { explicitArgCount: instruction.a, entries: instruction.extra };

          this.ensureStack(dispatch.explicitArgCount + 1);
          const args = new Array(dispatch.explicitArgCount);
          for (let i = dispatch.explicitArgCount - 1; i >= 0; i -= 1) {
            args[i] = this.stack.pop();
          }

          const target = this.stack.pop();
          if (!isVmObject(target)) {
            this.throwRuntime("InterfaceCall expects object target");
          }

          const entry = dispatch.entries.get(target.typeId);
          if (!entry) {
            this.throwRuntime(`No implementation for interface call on runtime object '${target.typeName}'`);
          }

          const totalArgCount = dispatch.explicitArgCount + 1;
          const newFrameSize = Math.max(entry.localCount, totalArgCount);
          const newFrameBase = this.localsTop;
          this.ensureLocalsCapacity(newFrameBase + newFrameSize);
          if (newFrameSize > totalArgCount) this.localsStack.fill(0, newFrameBase + totalArgCount, newFrameBase + newFrameSize);
          this.localsStack[newFrameBase] = target;
          for (let i = 0; i < args.length; i += 1) {
            this.localsStack[newFrameBase + i + 1] = args[i];
          }

          const callDepth = this.callStack.length;
          const callFrame = this.callFramePool[callDepth] ?? (this.callFramePool[callDepth] = {});
          callFrame.returnIp = this.ip;
          callFrame.callIp = callIp;
          callFrame.frameBase = this.frameBase;
          callFrame.frameSize = this.frameSize;
          callFrame.localsTop = this.localsTop;
          callFrame.allowFrameGrowth = this.allowFrameGrowth;
          this.callStack.push(callFrame);
          if (this.profiler.enabled) this.profiler.allocate("callFrames");
          this.frameBase = newFrameBase;
          this.frameSize = newFrameSize;
          this.allowFrameGrowth = false;
          this.localsTop = newFrameBase + newFrameSize;
          this.ip = entry.targetIp;
          if (this.profiler.enabled) this.profiler.enterFunction(entry.targetIp);
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
          const bindingId = instruction.a;
          const { symbol, arity: argCount } = this.metadata.hostBindings[bindingId];
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
          let args = this.hostArgumentBuffers.get(argCount);
          if (!args) {
            args = new Array(argCount);
            this.hostArgumentBuffers.set(argCount, args);
          }
          for (let i = argCount - 1; i >= 0; i -= 1) {
            args[i] = this.stack.pop();
          }

          const result = this.profiler.enabled
            ? this.profiler.measureHost(symbol, () => binding.handler(args))
            : binding.handler(args);
          this.stack.push(result ?? 0);
          break;
        }

        case OpCode.Halt:
          return;

        default:
          this.throwRuntime(`Unknown opcode ${op} at ${instructionIp}`);
      }
    }
  }

  popNumber() {
    this.ensureStack(1);
    const value = this.stack.pop();
    if (typeof value !== "number" || !Number.isFinite(value)) this.throwRuntime(`Expected number on stack, found ${typeof value}`);
    return value;
  }

  coerceNumericCastToInteger(allowNegative, targetType) {
    const value = this.popNumber();
    if (!Number.isFinite(value)) {
      this.throwRuntime(`Cannot cast non-finite value to ${targetType}`);
    }

    const truncated = Math.trunc(value);
    if (!allowNegative && truncated < 0) {
      this.throwRuntime("Cannot cast negative value to whole");
    }
    if (truncated < -2147483648 || truncated > 2147483647) {
      this.throwRuntime(`Cannot cast value outside integer range to ${targetType}`);
    }
    return truncated;
  }

  coerceCheckedSizedNumeric(kind) {
    const value = this.popNumber();
    const name = sizedNumericName(kind);
    if (!Number.isFinite(value)) {
      this.throwRuntime(`Cannot cast non-finite value to ${name}`);
    }

    if (kind === SizedNumericKind.Real32) {
      const rounded = Math.fround(value);
      if (!Number.isFinite(rounded)) {
        this.throwRuntime("Cannot cast value outside real32 range to real32");
      }
      return rounded;
    }

    const truncated = Math.trunc(value);
    const [minimum, maximum] = sizedNumericIntegralRange(kind);
    if (truncated < minimum || truncated > maximum) {
      this.throwRuntime(`Cannot cast value outside ${name} range`);
    }
    return truncated;
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
    if (index >= this.frameSize) {
      const oldTop = this.localsTop;
      this.frameSize = index + 1;
      this.localsTop = this.frameBase + this.frameSize;
      this.ensureLocalsCapacity(this.localsTop);
      this.localsStack.fill(0, oldTop, this.localsTop);
    }
  }

  ensureLocalsCapacity(size) {
    this.localsHighWater = Math.max(this.localsHighWater, size);
    while (this.localsStack.length < size) this.localsStack.push(0);
  }

  ensureGlobals(index) {
    if (index < 0) {
      this.throwRuntime(`Negative global index ${index}`);
    }
    while (index >= this.globals.length) {
      this.globals.push(0);
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

  readByteOperand() {
    this.ensureBytes(1);
    const value = this.bytes[this.ip];
    this.ip += 1;
    return value;
  }

  readLongOperand() {
    this.ensureBytes(8);
    const low = this.view.getUint32(this.ip, true);
    const high = this.view.getInt32(this.ip + 4, true);
    this.ip += 8;
    return (high * 4294967296) + low;
  }

  readDoubleOperand() {
    this.ensureBytes(8);
    const value = this.view.getFloat64(this.ip, true);
    this.ip += 8;
    return value;
  }

  readMetadataIndex(count, name) {
    const value = this.readIntOperand();
    if (value < 0 || value >= count) this.throwRuntime(`Bytecode ${name} index ${value} is out of range`);
    return value;
  }

  readInterfaceDispatchTable() {
    const explicitArgCount = this.readIntOperand();
    const entryCount = this.readIntOperand();
    const entries = new Map();
    for (let i = 0; i < entryCount; i += 1) {
      const runtimeTypeId = this.readMetadataIndex(this.metadata.types.length, "interface type");
      const targetIp = this.readIntOperand();
      const localCount = this.readIntOperand();
      entries.set(runtimeTypeId, { targetIp, localCount });
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

    const faultIp = this.currentInstructionIp ?? (this.ip - 1);
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

class WasmRuntimeProfiler {
  constructor(vm) { this.vm = vm; this.enabled = false; this.startedAtMs = performance.now(); this.elapsedMs = 0; }
  start() { this.reset(); this.enabled = true; this.vm.exports.code_vm_profile_set_enabled(this.vm.vmHandle, 1); this.startedAtMs = performance.now(); return this; }
  stop() {
    if (this.enabled) this.elapsedMs += performance.now() - this.startedAtMs;
    this.enabled = false; this.vm.exports.code_vm_profile_set_enabled(this.vm.vmHandle, 0); return this.report();
  }
  reset() {
    this.startedAtMs = performance.now(); this.elapsedMs = 0;
    if (this.vm.vmHandle > 0) this.vm.exports.code_vm_profile_reset(this.vm.vmHandle);
    return this;
  }
  report() {
    const elapsedMs = this.elapsedMs + (this.enabled ? performance.now() - this.startedAtMs : 0);
    const vm = this.vm, handle = vm.vmHandle;
    const opcodes = [];
    for (let opcode = 0; opcode <= 255; opcode += 1) {
      const count = handle > 0 ? vm.exports.code_vm_profile_opcode_count(handle, opcode) : 0;
      if (count > 0) opcodes.push({ opcode, name: OpCodeNames[opcode] ?? `0x${opcode.toString(16)}`, count });
    }
    const decodeName = (pointer, length) => Utf8Decoder.decode(new Uint8Array(vm.exports.memory.buffer, pointer, length));
    const functions = [];
    for (let index = 0; index < (handle > 0 ? vm.exports.code_vm_profile_function_count(handle) : 0); index += 1) {
      const calls = vm.exports.code_vm_profile_function_metric(handle, index, 0);
      if (calls <= 0) continue;
      functions.push({
        name: decodeName(vm.exports.code_vm_profile_function_name_pointer(handle, index), vm.exports.code_vm_profile_function_name_length(handle, index)),
        calls,
        inclusiveMs: vm.exports.code_vm_profile_function_metric(handle, index, 1),
        selfMs: vm.exports.code_vm_profile_function_metric(handle, index, 2)
      });
    }
    const hostCalls = [];
    for (let index = 0; index < (handle > 0 ? vm.exports.code_vm_profile_host_count(handle) : 0); index += 1) {
      const calls = vm.exports.code_vm_profile_host_metric(handle, index, 0);
      if (calls <= 0) continue;
      hostCalls.push({
        symbol: decodeName(vm.exports.code_vm_profile_host_name_pointer(handle, index), vm.exports.code_vm_profile_host_name_length(handle, index)),
        calls,
        totalMs: vm.exports.code_vm_profile_host_metric(handle, index, 1)
      });
    }
    const metric = index => handle > 0 ? vm.exports.code_vm_profile_metric(handle, index) : 0;
    return {
      enabled: this.enabled, elapsedMs,
      instructionCount: handle > 0 ? vm.exports.code_vm_profile_instruction_count(handle) : 0,
      decodedInstructionCount: handle > 0 ? vm.exports.code_vm_decoded_instruction_count(handle) : 0,
      opcodes, functions, hostCalls,
      allocations: { objects: metric(0), arrays: metric(1), callFrames: metric(2) },
      stackHighWater: metric(4), localsHighWater: metric(5), callStackHighWater: metric(6),
      garbageCollections: metric(7),
      runtime: "rust-wasm"
    };
  }
}

export class WasmWebVm {
  static async create(bytecodeBytes, wasmBytes, options = {}) {
    let adapter = null;
    const imports = { code_host: {
      call: (context, bindingId, argumentsPointer, argumentCount, resultPointer) =>
        adapter?.invokeHost(bindingId, argumentsPointer, argumentCount, resultPointer) ?? 1,
      output: (context, valuePointer) => adapter?.emitOutput(valuePointer),
      unix_milliseconds: () => Date.now(),
      monotonic_milliseconds: () => performance.now()
    } };
    const module = await WebAssembly.instantiate(wasmBytes, imports);
    adapter = new WasmWebVm(module.instance, bytecodeBytes, options);
    adapter.initialize();
    return adapter;
  }

  constructor(instance, bytecodeBytes, options) {
    this.instance = instance;
    this.exports = instance.exports;
    this.bytes = bytecodeBytes instanceof Uint8Array ? bytecodeBytes : new Uint8Array(bytecodeBytes);
    this.output = typeof options.output === "function" ? options.output : line => console.log(line);
    this.sceneHost = options.sceneHost ?? null;
    this.metadata = readHeader(this.bytes).metadata;
    this.hostBridge = new WebVm(this.bytes, { output: this.output, sceneHost: this.sceneHost });
    this.profiler = new WasmRuntimeProfiler(this);
    this.profileInitiallyEnabled = options.profileEnabled === true;
    this.vmHandle = 0;
    this.bytecodePointer = 0;
  }

  initialize() {
    if (this.exports.code_value_size() !== 16) throw new Error("Rust/Wasm runtime value ABI mismatch.");
    this.bytecodePointer = this.exports.code_alloc(this.bytes.byteLength);
    new Uint8Array(this.exports.memory.buffer, this.bytecodePointer, this.bytes.byteLength).set(this.bytes);
    this.vmHandle = this.exports.code_vm_create(this.bytecodePointer, this.bytes.byteLength);
    this.exports.code_dealloc(this.bytecodePointer, this.bytes.byteLength);
    this.bytecodePointer = 0;
    if (this.vmHandle <= 0) throw new Error(`Rust/Wasm VM initialization failed with status ${-this.vmHandle}.`);
    if (this.profileInitiallyEnabled) this.profiler.start();
  }

  run() { this.checkStatus(this.exports.code_vm_run(this.vmHandle), "module initialization"); }

  createObject(typeIdOrName) {
    const typeId = typeof typeIdOrName === "number" ? typeIdOrName : this.metadata.types.findIndex(type => type.name === typeIdOrName);
    if (typeId < 0) throw new Error(`Unknown object type '${typeIdOrName}'.`);
    const handle = this.exports.code_vm_create_object(this.vmHandle, typeId);
    if (handle < 0) throw new Error(`Rust/Wasm object allocation failed with status ${-handle}.`);
    return { __wasmObjectHandle: handle, typeId };
  }

  invokeVoid(targetIp, frameSize, args = []) {
    if (args.length !== 1 || !args[0] || !Number.isInteger(args[0].__wasmObjectHandle)) {
      throw new Error("Rust/Wasm lifecycle invocation requires one scene object argument.");
    }
    this.checkStatus(
      this.exports.code_vm_invoke_object(this.vmHandle, targetIp, frameSize, args[0].__wasmObjectHandle),
      `lifecycle call at bytecode offset ${targetIp}`
    );
  }

  checkStatus(status, operation) {
    if (status === 0) return;
    if (status === 8 && this.lastHostError) {
      const error = this.lastHostError;
      this.lastHostError = null;
      throw error;
    }
    const messages = {
      1: "Invalid bytecode artifact.", 2: "Unsupported bytecode version.", 3: "Truncated bytecode artifact.",
      4: "Missing bytecode metadata.", 5: "Invalid bytecode metadata.", 6: "Invalid branch or call target.",
      7: "Unsupported bytecode opcode.", 8: "Unsupported host binding.", 9: "Expected numeric value.",
      10: "Expected array value.", 11: "Expected object value.", 12: "Operand stack underflow.",
      13: "Runtime value is out of range.", 14: "Runtime storage capacity exceeded.",
      15: "Division by zero in bytecode.", 16: "Modulo by zero in bytecode.", 17: "User error."
    };
    const ip = this.exports.code_vm_last_error_metric(this.vmHandle, 1);
    const line = this.exports.code_vm_last_error_metric(this.vmHandle, 2);
    const column = this.exports.code_vm_last_error_metric(this.vmHandle, 3);
    const message = messages[status] ?? `VM status ${status}.`;
    throw new VmRuntimeError(`${message} (${operation})`, { ip, line, column, callStack: [], error: { type: "RuntimeError", message, line, column } });
  }

  readValue(pointer) {
    const view = new DataView(this.exports.memory.buffer);
    const payload = Number(view.getBigUint64(pointer, true));
    const tag = view.getUint32(pointer + 12, true);
    if (tag === 0) return view.getFloat64(pointer, true);
    if (tag === 3) {
      const stringPointer = this.exports.code_active_string_pointer(payload);
      const length = this.exports.code_active_string_length(payload);
      return Utf8Decoder.decode(new Uint8Array(this.exports.memory.buffer, stringPointer, length));
    }
    if (tag === 1) {
      const length = this.exports.code_active_array_length(payload);
      return Array.from({ length }, (_, index) => this.exports.code_active_array_number(payload, index));
    }
    if (tag === 10) return OptionalNone;
    return { __wasmValueTag: tag, __wasmValueHandle: payload };
  }

  writeNumber(pointer, value) {
    const view = new DataView(this.exports.memory.buffer);
    view.setFloat64(pointer, Number(value), true);
    view.setUint32(pointer + 8, 0, true);
    view.setUint32(pointer + 12, 0, true);
  }

  invokeHost(bindingId, argumentsPointer, argumentCount, resultPointer) {
    try {
      const metadata = this.metadata.hostBindings[bindingId];
      const binding = metadata ? this.hostBridge.hostBindings.get(metadata.symbol) : null;
      if (!binding || binding.arity !== argumentCount) return 1;
      const args = Array.from({ length: argumentCount }, (_, index) => this.readValue(argumentsPointer + index * 16));
      const result = binding.handler(args);
      if (typeof result !== "number") return 1;
      this.writeNumber(resultPointer, result);
      return 0;
    } catch (error) {
      this.lastHostError = error;
      return 1;
    }
  }

  emitOutput(valuePointer) { this.output(String(this.readValue(valuePointer))); }

  dispose() {
    if (this.vmHandle > 0) this.exports.code_vm_destroy(this.vmHandle);
    this.vmHandle = 0;
  }
}

class DirectWasmProfiler {
  constructor(vm, enabled = false) {
    this.vm = vm;
    this.enabled = enabled;
    this.reset();
  }
  start() { this.reset(); this.enabled = true; this.startedAtMs = performance.now(); return this; }
  stop() { this.enabled = false; return this.report(); }
  reset() { this.startedAtMs = performance.now(); this.functions = new Map(); this.hosts = new Map(); return this; }
  recordFunction(name, elapsedMs) {
    if (!this.enabled) return;
    const value = this.functions.get(name) ?? { name, calls: 0, inclusiveMs: 0, selfMs: 0 };
    value.calls += 1; value.inclusiveMs += elapsedMs; value.selfMs += elapsedMs;
    this.functions.set(name, value);
  }
  recordHost(symbol, elapsedMs) {
    if (!this.enabled) return;
    const value = this.hosts.get(symbol) ?? { symbol, calls: 0, totalMs: 0 };
    value.calls += 1; value.totalMs += elapsedMs;
    this.hosts.set(symbol, value);
  }
  report() {
    return {
      backend: "direct-wasm",
      runtime: "direct-wasm",
      elapsedMs: performance.now() - this.startedAtMs,
      instructionCount: 0,
      decodedInstructionCount: 0,
      opcodes: [],
      typedOperations: [],
      functions: Array.from(this.functions.values()),
      hostCalls: Array.from(this.hosts.values()),
      allocations: { objects: 0, arrays: 0, frames: 0 },
      stackHighWater: 0,
      frameHighWater: 0,
      garbageCollections: 0,
      garbageCollectionDisabled: this.vm.directWasmOptions.garbageCollectionDisabled === true,
      garbageCollectionEnabled: false,
      garbageCollectionMode: this.vm.directWasmOptions.garbageCollectionMode ?? "bump"
    };
  }
}

export class DirectWasmWebVm {
  static async create(appWasmBytes, bridgeBytecode, options = {}) {
    let adapter = null;
    const strings = [""];
    const stringIds = new Map([["", 0]]);
    const stringHandle = value => {
      const text = String(value);
      if (stringIds.has(text)) return stringIds.get(text);
      strings.push(text);
      const handle = strings.length - 1;
      stringIds.set(text, handle);
      return handle;
    };
    const collections = [null];
    const runtimeBase = {
      string_from_utf8: (pointer, length) => {
        const bytes = new Uint8Array(adapter.exports.memory.buffer, pointer, length);
        return stringHandle(Utf8Decoder.decode(bytes));
      },
      string_concat: (left, right) => stringHandle((strings[left] ?? "") + (strings[right] ?? "")),
      string_equal: (left, right) => (strings[left] ?? "") === (strings[right] ?? "") ? 1 : 0,
      string_from_i32: value => stringHandle(value ? "true" : "false"),
      string_from_i64: value => stringHandle(String(value)),
      string_from_f64: value => stringHandle(String(value)),
      collection_new: kind => { collections.push({ kind, items: [], map: new Map(), set: new Set() }); return collections.length - 1; },
      collection_length: handle => BigInt(collections[handle]?.kind === 1 ? collections[handle].map.size : collections[handle]?.kind === 2 ? collections[handle].set.size : collections[handle]?.items.length ?? 0)
    };
    const runtimeImports = new Proxy(runtimeBase, {
      get(target, name) {
        if (name in target) return target[name];
        const operation = String(name);
        if (operation.startsWith("map_set_")) return (handle, key, value) => collections[handle].map.set(key, value);
        if (operation.startsWith("map_get_")) return (handle, key) => collections[handle].map.get(key);
        if (operation.startsWith("collection_add_")) return (handle, value) => {
          const collection = collections[handle];
          if (collection.kind === 2) collection.set.add(value); else collection.items.push(value);
        };
        if (operation.startsWith("collection_contains_")) return (handle, value) => {
          const collection = collections[handle];
          return (collection.kind === 1 ? collection.map.has(value) : collection.kind === 2 ? collection.set.has(value) : collection.items.includes(value)) ? 1 : 0;
        };
        if (operation.startsWith("collection_remove_")) return (handle, value) => {
          const collection = collections[handle];
          if (collection.kind === 1) collection.map.delete(value);
          else if (collection.kind === 2) collection.set.delete(value);
          else { const index = collection.items.indexOf(value); if (index >= 0) collection.items.splice(index, 1); }
        };
        if (operation.startsWith("collection_peek_")) return handle => {
          const collection = collections[handle];
          return collection.kind === 4 ? collection.items[collection.items.length - 1] : collection.items[0];
        };
        if (operation.startsWith("collection_pop_")) return handle => {
          const collection = collections[handle];
          return collection.kind === 4 ? collection.items.pop() : collection.items.shift();
        };
        return undefined;
      }
    });
    const directPrint = value => options.output?.(String(value));
    const hostImports = new Proxy({
      print_i32: directPrint,
      print_i64: directPrint,
      print_f64: directPrint,
      print_string: handle => directPrint(strings[handle] ?? ""),
      panic_string: handle => { throw new Error(strings[handle] ?? "Direct-Wasm panic"); }
    }, {
      get(target, symbol) {
        if (symbol in target) return target[symbol];
        return (...args) => adapter.invokeHost(String(symbol), args);
      }
    });
    const module = await WebAssembly.instantiate(appWasmBytes, { code_host: hostImports, code_runtime: runtimeImports });
    adapter = new DirectWasmWebVm(module.instance, bridgeBytecode, strings, options);
    return adapter;
  }

  constructor(instance, bridgeBytecode, strings, options) {
    this.instance = instance;
    this.exports = instance.exports;
    this.strings = strings;
    this.sceneInfo = options.sceneInfo;
    this.directWasmOptions = options.directWasmOptions ?? {};
    this.output = typeof options.output === "function" ? options.output : line => console.log(line);
    this.hostBridge = new WebVm(bridgeBytecode, { output: this.output, sceneHost: options.sceneHost });
    this.profiler = new DirectWasmProfiler(this, options.profileEnabled === true);
    if (this.profiler.enabled) this.profiler.start();
  }

  run() {
    const status = this.exports.code_run();
    if (status !== 0) throw new Error(`Direct-Wasm initialization failed with status ${status}.`);
  }

  createObject() { return typeof this.exports.code_scene_new === "function" ? this.exports.code_scene_new() : 0; }

  invokeVoid(targetIp, _frameSize, args = []) {
    const scene = this.sceneInfo;
    let name;
    let callable;
    if (targetIp === scene.constructor.targetIp) return;
    if (targetIp === scene.start.targetIp) { name = "start"; callable = this.exports.code_start; }
    else if (targetIp === scene.update.targetIp) { name = "update"; callable = this.exports.code_update; }
    else if (targetIp === scene.draw.targetIp) { name = "draw"; callable = this.exports.code_draw; }
    else if (scene.drawHud && targetIp === scene.drawHud.targetIp) { name = "drawHud"; callable = this.exports.code_draw_hud; }
    if (typeof callable !== "function") throw new Error(`Direct-Wasm lifecycle export is missing for bytecode offset ${targetIp}.`);
    const startedAt = this.profiler.enabled ? performance.now() : 0;
    if (callable.length > 0) callable(args[0] ?? 0);
    else callable();
    if (this.profiler.enabled) this.profiler.recordFunction(name, performance.now() - startedAt);
  }

  invokeHost(symbol, rawArguments) {
    const binding = this.hostBridge.hostBindings.get(symbol);
    if (!binding) throw new Error(`Direct-Wasm host binding '${symbol}' is unavailable.`);
    const args = rawArguments.map(value => typeof value === "bigint" ? Number(value) : value);
    this.convertHostReferences(symbol, args);
    const startedAt = this.profiler.enabled ? performance.now() : 0;
    const result = binding.handler(args);
    if (this.profiler.enabled) this.profiler.recordHost(symbol, performance.now() - startedAt);
    return this.hostReturnsI64(symbol) ? BigInt(Math.trunc(Number(result) || 0)) : Number(result) || 0;
  }

  convertHostReferences(symbol, args) {
    const stringIndexes = {
      "engine.window.create": [0],
      "engine.gfx.draw_text_scene": [0, 4, 5],
      "engine.gfx.draw_image_scene": [0],
      "engine.gfx.draw_sprite_scene": [0],
      "engine.audio.play_sound_scene": [0],
      "engine.audio.play_looping_sound_scene": [0]
    }[symbol] ?? [];
    for (const index of stringIndexes) args[index] = this.strings[args[index]] ?? "";
    if (symbol === "engine.gfx.draw_polygon_scene" || symbol === "engine.gfx.draw_polygon_outline_scene")
      args[0] = this.readRealArray(args[0]);
  }

  readRealArray(pointer) {
    if (!pointer) return [];
    const view = new DataView(this.exports.memory.buffer);
    const length = view.getInt32(pointer, true);
    const data = view.getInt32(pointer + 8, true);
    return Array.from({ length }, (_, index) => view.getFloat64(data + index * 8, true));
  }

  hostReturnsI64(symbol) {
    return symbol.startsWith("std.time.") || symbol === "std.math.sign" ||
      symbol === "engine.window.create" || symbol === "engine.audio.play_sound_scene" ||
      symbol === "engine.audio.play_looping_sound_scene" ||
      symbol === "engine.diagnostics.last_update_steps_scene" ||
      symbol === "engine.diagnostics.last_dropped_update_steps_scene";
  }

  dispose() {}
}

export class WorkerCodeRuntimeController {
  constructor(runtime, workerSource, bytecode, wasmBytes, sceneInfo, profileEnabled = false, backend = "wasm-vm", appWasm = null, directWasmOptions = {}) {
    this.runtime = runtime;
    runtime.workerController = this;
    this.sceneInfo = sceneInfo;
    this.profileEnabled = profileEnabled;
    this.frameInFlight = false;
    this.nextFrameId = 1;
    this.lastRequestedFrameTimestamp = 0;
    this.pendingRequests = new Map();
    this.nextRequestId = 1;
    this.audioHandleMap = new Map();
    this.lastCompletedFrameId = 0;
    this.running = true;
    this.tick = timestamp => this.onAnimationFrame(timestamp);
    const blob = new Blob([workerSource], { type: "text/javascript" });
    this.workerUrl = URL.createObjectURL(blob);
    this.worker = new Worker(this.workerUrl, { name: "code-runtime" });
    this.worker.onmessage = event => this.onWorkerMessage(event.data);
    this.worker.onerror = event => this.onWorkerFailure(event.error ?? new Error(event.message));
    runtime.audioStatusChanged = (mainHandle, playing) => {
      for (const [workerHandle, mappedMainHandle] of this.audioHandleMap) {
        if (mappedMainHandle === mainHandle) {
          this.worker.postMessage({ type: "audioStatus", handle: workerHandle, playing });
          break;
        }
      }
    };
    const sourceBytes = bytecode instanceof Uint8Array ? bytecode : new Uint8Array(bytecode);
    const transferred = sourceBytes.buffer.slice(sourceBytes.byteOffset, sourceBytes.byteOffset + sourceBytes.byteLength);
    const sourceWasm = wasmBytes instanceof Uint8Array ? wasmBytes : new Uint8Array(wasmBytes);
    const transferredWasm = sourceWasm.buffer.slice(sourceWasm.byteOffset, sourceWasm.byteOffset + sourceWasm.byteLength);
    const appSource = appWasm ? (appWasm instanceof Uint8Array ? appWasm : new Uint8Array(appWasm)) : null;
    const transferredApp = appSource?.buffer.slice(appSource.byteOffset, appSource.byteOffset + appSource.byteLength) ?? null;
    const transfers = transferredApp ? [transferred, transferredWasm, transferredApp] : [transferred, transferredWasm];
    this.worker.postMessage({ type: "init", backend, bytecode: transferred, wasm: transferredWasm, appWasm: transferredApp, sceneInfo, profileEnabled, directWasmOptions }, transfers);
  }

  viewportSnapshot() {
    const r = this.runtime;
    return {
      safeLeft: r.safeLeft, safeTop: r.safeTop, safeWidth: r.safeWidth, safeHeight: r.safeHeight,
      viewLeft: r.viewLeft, viewTop: r.viewTop, viewWidth: r.viewWidth, viewHeight: r.viewHeight,
      viewportWidth: r.viewportWidth, viewportHeight: r.viewportHeight, worldScale: r.worldScale
    };
  }

  inputSnapshot() {
    const r = this.runtime;
    const snapshot = {
      keysDown: Array.from(r.keysDown), pointerScreenX: r.pointerScreenX, pointerScreenY: r.pointerScreenY,
      pointerIsDownNow: r.pointerIsDownNow, pointerPressed: r.pointerPressedPending,
      pointerReleased: r.pointerReleasedPending, audioUnlocked: r.audioUnlocked
    };
    r.pointerPressedPending = false;
    r.pointerReleasedPending = false;
    return snapshot;
  }

  sendInputSnapshot() {
    if (this.running) this.worker.postMessage({ type: "input", input: this.inputSnapshot() });
  }

  sendViewportSnapshot() {
    if (this.running) this.worker.postMessage({ type: "viewport", viewport: this.viewportSnapshot() });
  }

  onAnimationFrame(timestamp) {
    this.runtime.frameHandle = 0;
    if (!this.running) return;
    const cap = this.runtime.maximumRenderRate;
    const due = cap <= 0 || timestamp - this.lastRequestedFrameTimestamp >= 1000 / cap;
    if (due && !this.frameInFlight) {
      this.frameInFlight = true;
      this.lastRequestedFrameTimestamp = timestamp;
      this.worker.postMessage({
        type: "frame", id: this.nextFrameId++, timestamp,
        viewport: this.viewportSnapshot(), input: this.inputSnapshot()
      });
    }
    this.runtime.frameHandle = requestAnimationFrame(this.tick);
  }

  onWorkerMessage(message) {
    switch (message?.type) {
      case "ready":
        if (typeof document !== "undefined") document.body.dataset.codeRuntime = "ready";
        this.runtime.frameHandle = requestAnimationFrame(this.tick);
        break;
      case "frame":
        this.frameInFlight = false;
        if (message.id <= this.lastCompletedFrameId) break;
        this.lastCompletedFrameId = message.id;
        replayDrawCommands(this.runtime, message.commands, message.strings);
        this.applyDiagnostics(message.diagnostics);
        if (typeof document !== "undefined") document.body.dataset.codeRuntime = "frame";
        break;
      case "output": console.log(message.line); break;
      case "config":
        if (message.maximumRenderRate !== undefined) this.runtime.maximumRenderRate = message.maximumRenderRate;
        break;
      case "audio": this.handleAudio(message); break;
      case "response": {
        const request = this.pendingRequests.get(message.id);
        if (request) { this.pendingRequests.delete(message.id); message.error ? request.reject(new Error(message.error)) : request.resolve(message.value); }
        break;
      }
      case "fatal": this.onWorkerFailure(deserializeWorkerError(message.error)); break;
    }
  }

  applyDiagnostics(value = {}) {
    const r = this.runtime;
    r.lastFrameIntervalMs = value.frameIntervalMs ?? r.lastFrameIntervalMs;
    r.lastFrameWorkMs = value.frameWorkMs ?? r.lastFrameWorkMs;
    r.lastUpdateWorkMs = value.updateWorkMs ?? r.lastUpdateWorkMs;
    r.lastDrawWorkMs = value.drawWorkMs ?? r.lastDrawWorkMs;
    r.lastDrawHudWorkMs = value.drawHudWorkMs ?? r.lastDrawHudWorkMs;
    r.lastUpdateStepsCount = value.updateSteps ?? r.lastUpdateStepsCount;
    r.lastDroppedUpdateStepsCount = value.droppedUpdateSteps ?? r.lastDroppedUpdateStepsCount;
    r.lastUpdateIntervalMs = value.updateIntervalMs ?? r.lastUpdateIntervalMs;
    r.lastUpdateDeltaMs = value.updateDeltaMs ?? r.lastUpdateDeltaMs;
  }

  handleAudio(message) {
    if (message.action === "play") {
      const mainHandle = message.loop
        ? this.runtime.playLoopingSound(message.source, message.volume)
        : this.runtime.playSound(message.source, message.volume);
      this.audioHandleMap.set(message.handle, mainHandle);
      this.worker.postMessage({
        type: "audioStatus", handle: message.handle,
        playing: this.runtime.soundIsPlaying(mainHandle)
      });
    } else if (message.action === "stop") {
      this.runtime.stopSound(this.audioHandleMap.get(message.handle) ?? 0);
      this.audioHandleMap.delete(message.handle);
    } else if (message.action === "volume") {
      this.runtime.setSoundVolume(this.audioHandleMap.get(message.handle) ?? 0, message.volume);
    } else if (message.action === "stopAll") {
      this.runtime.stopAllSounds();
      this.audioHandleMap.clear();
    }
  }

  request(action) {
    const id = this.nextRequestId++;
    return new Promise((resolve, reject) => {
      this.pendingRequests.set(id, { resolve, reject });
      this.worker.postMessage({ type: "request", id, action });
    });
  }

  profileStart() { return this.request("profile-start"); }
  profileStop() { return this.request("profile-stop"); }
  profileReset() { return this.request("profile-reset"); }
  async profileReport(print = true) {
    const report = await this.request("profile-report");
    if (print) {
      console.log("Code runtime profile", report);
      console.table(report.opcodes); console.table(report.functions); console.table(report.hostCalls);
    }
    return report;
  }
  async profileJson() { return JSON.stringify(await this.profileReport(false), null, 2); }

  setHidden(hidden) {
    this.worker.postMessage({ type: "visibility", hidden, timestamp: performance.now() });
    if (hidden) {
      if (this.runtime.frameHandle) cancelAnimationFrame(this.runtime.frameHandle);
      this.runtime.frameHandle = 0;
      this.frameInFlight = false;
      return;
    }
    this.lastRequestedFrameTimestamp = 0;
    if (this.running && !this.runtime.frameHandle) this.runtime.frameHandle = requestAnimationFrame(this.tick);
  }
  dispose() {
    this.running = false;
    if (this.runtime.frameHandle) cancelAnimationFrame(this.runtime.frameHandle);
    this.runtime.audioStatusChanged = null;
    this.runtime.workerController = null;
    this.worker.terminate();
    URL.revokeObjectURL(this.workerUrl);
  }
  onWorkerFailure(error) {
    if (typeof document !== "undefined") document.body.dataset.codeRuntime = "fatal";
    this.dispose(); this.runtime.showFatal(error); console.error(error);
  }
}

function deserializeWorkerError(value) {
  const error = new Error(value?.message ?? "Worker runtime failed.");
  error.name = value?.name ?? "Error";
  error.stack = value?.stack ?? error.stack;
  error.payload = value?.payload ?? null;
  return error;
}

function installCodeWorkerRuntime() {
  let runtime = null;
  let vm = null;
  const respond = (id, value, error = null) => self.postMessage({ type: "response", id, value, error });
  self.onmessage = async event => {
    const message = event.data;
    try {
      switch (message?.type) {
        case "init":
          runtime = new WorkerSceneRuntime((value, transfer = []) => self.postMessage(value, transfer));
          if (message.backend === "direct-wasm") {
            vm = await DirectWasmWebVm.create(message.appWasm, new Uint8Array(message.bytecode), {
              output: line => self.postMessage({ type: "output", line: String(line) }),
              sceneHost: runtime, sceneInfo: message.sceneInfo, profileEnabled: message.profileEnabled === true,
              directWasmOptions: message.directWasmOptions ?? {}
            });
          } else {
            vm = await WasmWebVm.create(new Uint8Array(message.bytecode), message.wasm, {
              output: line => self.postMessage({ type: "output", line: String(line) }),
              sceneHost: runtime, profileEnabled: message.profileEnabled === true
            });
          }
          runtime.initializeWorkerScene(vm, message.sceneInfo);
          self.postMessage({ type: "ready" });
          break;
        case "frame": runtime.renderWorkerFrame(message.id, message.timestamp, message.viewport, message.input); break;
        case "input": runtime.applyInput(message.input); break;
        case "viewport": runtime.applyViewport(message.viewport); break;
        case "audioStatus": runtime.applyAudioStatus(message.handle, message.playing === true); break;
        case "visibility":
          runtime.setWorkerHidden(message.hidden);
          break;
        case "request": {
          let value;
          if (message.action === "profile-start") value = vm.profiler.start().report();
          else if (message.action === "profile-stop") value = vm.profiler.stop();
          else if (message.action === "profile-reset") value = vm.profiler.reset().report();
          else if (message.action === "profile-report") value = vm.profiler.report();
          else throw new Error(`Unknown worker request '${message.action}'.`);
          respond(message.id, value);
          break;
        }
      }
    } catch (error) {
      if (message?.type === "request") respond(message.id, null, error?.message ?? String(error));
      else self.postMessage({ type: "fatal", error: serializeWorkerError(error) });
    }
  };
}
