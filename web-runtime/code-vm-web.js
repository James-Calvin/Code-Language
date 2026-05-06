const BYTECODE_MAGIC = "CODE";
const BYTECODE_VERSION = 8;
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
  Halt: 0xff
};

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

function isVmQueue(value) {
  return value && typeof value === "object" && value.__vmQueue === true && Array.isArray(value.items);
}

function isVmStack(value) {
  return value && typeof value === "object" && value.__vmStack === true && Array.isArray(value.items);
}

function isVmObject(value) {
  return value && typeof value === "object" && value.__vmObject === true && value.fields instanceof Map;
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

function createVmObject(typeName, isRecord = false) {
  return { __vmObject: true, typeName, isRecord, fields: new Map() };
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
      if (left.typeName !== right.typeName || left.fields.size !== right.fields.size) {
        return false;
      }

      for (const [fieldName, leftValue] of left.fields.entries()) {
        if (!right.fields.has(fieldName)) {
          return false;
        }
        if (!valueEquals(leftValue, right.fields.get(fieldName))) {
          return false;
        }
      }

      return true;
    }

    return false;
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
    const fieldNames = Array.from(value.fields.keys()).sort();
    for (const fieldName of fieldNames) {
      hash = combineHash(hash, stringHash(fieldName));
      hash = combineHash(hash, valueHash(value.fields.get(fieldName)));
    }
    return hash;
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
    this.stepMs = 1000 / 60;
    this.accumulatorMs = 0;
    this.lastTimestampMs = 0;
    this.running = false;
    this.frameHandle = 0;
    this.sceneObject = null;
    this.sceneInfo = null;
    this.vm = null;
    this.lastFrameIntervalMs = 0;
    this.lastFrameWorkMs = 0;
    this.lastUpdateWorkMs = 0;
    this.lastDrawWorkMs = 0;
    this.lastDrawHudWorkMs = 0;
    this.lastUpdateStepsCount = 0;
    this.appControlKeyCodes = new Set([32, 33, 34, 35, 36, 37, 38, 39, 40]);
    this.canvas = null;
    this.ctx = null;
    this.outputElement = null;
    this.imageCache = new Map();
    this.audioHandles = new Map();
    this.pendingAudioHandles = new Set();
    this.nextAudioHandle = 1;
    this.audioUnlocked = false;
    this.handleResize = () => this.resize();
    this.handleKeyDown = event => {
      if (this.shouldPreventBrowserKeyDefault(event)) {
        event.preventDefault();
      }
      this.unlockAudio();
      this.keysDown.add(event.keyCode);
    };
    this.handleKeyUp = event => {
      if (this.shouldPreventBrowserKeyDefault(event)) {
        event.preventDefault();
      }
      this.keysDown.delete(event.keyCode);
    };
    this.handleBlur = () => {
      this.keysDown.clear();
      this.cancelActivePointer();
    };
    this.handlePointerDown = event => this.onPointerDown(event);
    this.handlePointerMove = event => this.onPointerMove(event);
    this.handlePointerUp = event => this.onPointerUp(event);
    this.handlePointerCancel = event => this.onPointerCancel(event);
    this.handleContextMenu = event => event.preventDefault();
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
  }

  onPointerMove(event) {
    if (!this.shouldTrackPointerMove(event)) {
      return;
    }

    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
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
  }

  onPointerCancel(event) {
    if (this.pointerActiveId !== null && event.pointerId !== this.pointerActiveId) {
      return;
    }

    this.preventPointerDefault(event);
    this.updatePointerPosition(event);
    this.releaseActivePointer(event.pointerId);
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

  publishDiagnostics(frameIntervalMs, frameWorkMs, updateWorkMs, drawWorkMs, drawHudWorkMs, updateSteps) {
    this.lastFrameIntervalMs = frameIntervalMs;
    this.lastFrameWorkMs = frameWorkMs;
    this.lastUpdateWorkMs = updateWorkMs;
    this.lastDrawWorkMs = drawWorkMs;
    this.lastDrawHudWorkMs = drawHudWorkMs;
    this.lastUpdateStepsCount = updateSteps;
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
        console.warn(`Could not load audio '${normalizedSource}'.`);
      });
      audio.addEventListener("ended", () => {
        if (!record.loop) {
          record.stopped = true;
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
    if (playResult && typeof playResult.catch === "function") {
      playResult.catch(error => {
        if (record.stopped) {
          return;
        }

        if (error && error.name === "NotAllowedError") {
          record.pending = true;
          this.pendingAudioHandles.add(record.handle);
          this.audioUnlocked = false;
          return;
        }

        record.failed = true;
        record.pending = false;
        this.pendingAudioHandles.delete(record.handle);
        const message = error && typeof error.message === "string" ? error.message : String(error);
        console.warn(`Could not play audio '${record.source}': ${message}`);
      });
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
    this.publishDiagnostics(0, 0, 0, 0, 0, 0);
    this.frameHandle = requestAnimationFrame(this.tick);
  }

  stop() {
    this.running = false;
    if (this.frameHandle) {
      cancelAnimationFrame(this.frameHandle);
      this.frameHandle = 0;
    }
    this.stopAllSounds();
  }

  onFrame(timestamp) {
    if (!this.running || !this.vm || !this.sceneInfo || !this.sceneObject) {
      return;
    }

    try {
      const frameIntervalMs = Math.max(0, timestamp - this.lastTimestampMs);
      const elapsedMs = Math.min(250, frameIntervalMs);
      this.lastTimestampMs = timestamp;
      this.accumulatorMs += elapsedMs;

      const frameWorkStartMs = performance.now();
      let updateWorkMs = 0;
      let updateSteps = 0;
      let drawWorkMs = 0;
      let drawHudWorkMs = 0;

      this.setDrawSpace("world");
      while (this.accumulatorMs >= this.stepMs) {
        this.beginFixedUpdateStep();
        const updateStartMs = performance.now();
        this.vm.invokeVoid(this.sceneInfo.update.targetIp, this.sceneInfo.update.frameSize, [this.sceneObject]);
        updateWorkMs += performance.now() - updateStartMs;
        updateSteps += 1;
        this.accumulatorMs -= this.stepMs;
      }

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
        performance.now() - frameWorkStartMs,
        updateWorkMs,
        drawWorkMs,
        drawHudWorkMs,
        updateSteps);
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

  createObject(typeName) {
    return createVmObject(typeName, false);
  }

  createRecord(typeName) {
    return createVmObject(typeName, true);
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

        case OpCode.PushReal:
          this.stack.push(this.readDoubleOperand());
          break;

        case OpCode.PushWideInteger:
          this.stack.push(this.readLongOperand());
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

        case OpCode.IntDiv:
          this.integralBinary((a, b) => {
            if (b === 0) {
              this.throwRuntime("Division by zero in bytecode.");
            }
            return Math.trunc(a / b);
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
          this.stack.push(valueEquals(left, right) ? 1 : 0);
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
          this.stack.push(this.coerceCheckedSizedNumeric(this.readByteOperand()));
          break;

        case OpCode.NewObject: {
          const typeName = this.readStringOperand();
          this.stack.push(createVmObject(typeName, false));
          break;
        }

        case OpCode.NewRecord: {
          const typeName = this.readStringOperand();
          this.stack.push(createVmObject(typeName, true));
          break;
        }

        case OpCode.GetField: {
          const fieldName = this.readStringOperand();
          this.ensureStack(1);
          const target = this.stack.pop();
          if (!isVmObject(target)) {
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
          if (!isVmObject(target)) {
            this.throwRuntime("SetField expects object");
          }
          target.fields.set(fieldName, value);
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
          if (!isVmObject(target)) {
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

  integralBinary(operation) {
    const b = Math.trunc(this.popNumber());
    const a = Math.trunc(this.popNumber());
    this.stack.push(operation(a, b));
  }

  popNumber() {
    this.ensureStack(1);
    const value = this.stack.pop();
    return toNumber(value, message => this.throwRuntime(message));
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
