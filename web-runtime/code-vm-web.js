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
  ArrayAppend: 0x2b,
  ArrayRemoveAt: 0x2c,
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
    this.stepMs = 1000 / 60;
    this.accumulatorMs = 0;
    this.lastTimestampMs = 0;
    this.running = false;
    this.frameHandle = 0;
    this.sceneObject = null;
    this.sceneInfo = null;
    this.vm = null;
    this.canvas = null;
    this.ctx = null;
    this.outputElement = null;
    this.imageCache = new Map();
    this.handleResize = () => this.resize();
    this.handleKeyDown = event => {
      this.keysDown.add(event.keyCode);
    };
    this.handleKeyUp = event => {
      this.keysDown.delete(event.keyCode);
    };
    this.handleBlur = () => {
      this.keysDown.clear();
    };
    this.tick = timestamp => this.onFrame(timestamp);
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
    window.addEventListener("keydown", this.handleKeyDown);
    window.addEventListener("keyup", this.handleKeyUp);
    window.addEventListener("blur", this.handleBlur);
    this.resize();
  }

  dispose() {
    this.stop();
    window.removeEventListener("resize", this.handleResize);
    window.removeEventListener("keydown", this.handleKeyDown);
    window.removeEventListener("keyup", this.handleKeyUp);
    window.removeEventListener("blur", this.handleBlur);
    this.keysDown.clear();
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
  }

  appendOutput(line) {
    if (!this.outputElement) {
      return;
    }

    this.outputElement.style.display = "block";
    this.outputElement.textContent += `${line}\n`;
    this.outputElement.scrollTop = this.outputElement.scrollHeight;
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
    this.frameHandle = requestAnimationFrame(this.tick);
  }

  stop() {
    this.running = false;
    if (this.frameHandle) {
      cancelAnimationFrame(this.frameHandle);
      this.frameHandle = 0;
    }
  }

  onFrame(timestamp) {
    if (!this.running || !this.vm || !this.sceneInfo || !this.sceneObject) {
      return;
    }

    try {
      const elapsedMs = Math.min(250, Math.max(0, timestamp - this.lastTimestampMs));
      this.lastTimestampMs = timestamp;
      this.accumulatorMs += elapsedMs;

      this.setDrawSpace("world");
      while (this.accumulatorMs >= this.stepMs) {
        this.vm.invokeVoid(this.sceneInfo.update.targetIp, this.sceneInfo.update.frameSize, [this.sceneObject]);
        this.accumulatorMs -= this.stepMs;
      }

      this.setDrawSpace("world");
      this.vm.invokeVoid(this.sceneInfo.draw.targetIp, this.sceneInfo.draw.frameSize, [this.sceneObject]);
      if (this.sceneInfo.drawHud) {
        this.setDrawSpace("hud");
        this.vm.invokeVoid(this.sceneInfo.drawHud.targetIp, this.sceneInfo.drawHud.frameSize, [this.sceneObject]);
      }
      this.setDrawSpace("world");
      this.frameHandle = requestAnimationFrame(this.tick);
    } catch (error) {
      this.stop();
      this.showFatal(error);
      console.error(error);
    }
  }
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
    this.sceneHost = options.sceneHost ?? null;

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

  createObject(typeName) {
    return { __vmObject: true, typeName, fields: new Map() };
  }

  invokeVoid(targetIp, localCount, args = []) {
    const savedIp = this.ip;
    const savedLocals = this.locals;
    const savedStackDepth = this.stack.length;
    const savedCallStackDepth = this.callStack.length;

    const frameSize = Math.max(localCount || 0, args.length, 1);
    const locals = new Array(frameSize).fill(0);
    for (let i = 0; i < args.length; i += 1) {
      locals[i] = args[i];
    }

    this.locals = locals;
    this.ip = targetIp;

    try {
      this.run();
    } finally {
      this.locals = savedLocals;
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
