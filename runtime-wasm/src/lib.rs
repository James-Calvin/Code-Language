//! Dependency-free bytecode-v11 interpreter used by the Rust/Wasm performance gate.
//!
//! This crate deliberately exposes a small raw Wasm ABI. Browser integration can
//! copy an artifact into linear memory and run it without a binding generator or
//! any JavaScript calls from the dispatch loop.

use std::mem;
use std::slice;
use std::cell::RefCell;
use std::collections::HashSet;

const HEADER_SIZE: usize = 13;
const BYTECODE_VERSION: u8 = 12;
const ROOT_FRAME_SIZE: usize = 32;

const TAG_NUMBER: u32 = 0;
const TAG_ARRAY: u32 = 1;
const TAG_OBJECT: u32 = 2;
const TAG_STRING: u32 = 3;
const TAG_RECORD: u32 = 4;
const TAG_MAP: u32 = 5;
const TAG_SET: u32 = 6;
const TAG_QUEUE: u32 = 7;
const TAG_STACK: u32 = 8;
const TAG_FALLIBLE: u32 = 9;
const TAG_OPTIONAL_NONE: u32 = 10;

const HOST_UNSUPPORTED: u8 = 0;
const HOST_PRINT: u8 = 1;
const HOST_SQUARE_ROOT: u8 = 2;

#[repr(C, align(8))]
#[derive(Clone, Copy, Default)]
struct Value {
    payload: u64,
    auxiliary: u32,
    tag: u32,
}

impl Value {
    #[inline(always)]
    fn number(value: f64) -> Self {
        Self { payload: value.to_bits(), auxiliary: 0, tag: TAG_NUMBER }
    }

    #[inline(always)]
    fn number_value(self) -> Result<f64, VmError> {
        if self.tag != TAG_NUMBER {
            return Err(VmError::ExpectedNumber);
        }
        Ok(f64::from_bits(self.payload))
    }

    #[inline(always)]
    fn handle(tag: u32, handle: usize) -> Self {
        Self { payload: handle as u64, auxiliary: 0, tag }
    }
}

#[derive(Clone, Copy, Default)]
struct Instruction {
    real_bits: u64,
    a: i32,
    b: i32,
    c: i32,
    op: u8,
}

#[derive(Clone, Copy, Default)]
struct DispatchEntry {
    type_id: usize,
    target: usize,
    frame_size: usize,
}

#[derive(Clone)]
struct HostBinding {
    kind: u8,
    arity: usize,
    symbol: Vec<u8>,
}

#[derive(Clone)]
struct TypeInfo {
    name: Vec<u8>,
    is_record: bool,
    hash_field_slots: Vec<usize>,
}

#[derive(Clone)]
struct CallableInfo { target: usize, name: Vec<u8> }

#[derive(Clone, Copy, Default)]
struct FunctionProfile { calls: u64, inclusive_ms: f64, self_ms: f64 }

#[derive(Clone, Copy, Default)]
struct TimingFrame { function: usize, started_ms: f64, child_ms: f64 }

#[derive(Clone, Copy, Default)]
struct CallFrame {
    return_ip: usize,
    call_byte_ip: usize,
    frame_base: usize,
    frame_size: usize,
    locals_top: usize,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum VmError {
    InvalidArtifact,
    UnsupportedVersion,
    Truncated,
    MissingMetadata,
    InvalidMetadata,
    InvalidTarget,
    UnsupportedOpcode,
    UnsupportedHost,
    ExpectedNumber,
    ExpectedArray,
    ExpectedObject,
    StackUnderflow,
    Bounds,
    Capacity,
    DivisionByZero,
    ModuloByZero,
    UserError,
    MapKeyNotFound,
    ArrayIndexOutOfRange,
    QueueEmpty,
    StackEmpty,
    OptionalNone,
}

impl VmError {
    fn status(self) -> i32 {
        match self {
            Self::InvalidArtifact => 1,
            Self::UnsupportedVersion => 2,
            Self::Truncated => 3,
            Self::MissingMetadata => 4,
            Self::InvalidMetadata => 5,
            Self::InvalidTarget => 6,
            Self::UnsupportedOpcode => 7,
            Self::UnsupportedHost => 8,
            Self::ExpectedNumber => 9,
            Self::ExpectedArray => 10,
            Self::ExpectedObject => 11,
            Self::StackUnderflow => 12,
            Self::Bounds => 13,
            Self::Capacity => 14,
            Self::DivisionByZero => 15,
            Self::ModuloByZero => 16,
            Self::UserError => 17,
            Self::MapKeyNotFound => 18,
            Self::ArrayIndexOutOfRange => 19,
            Self::QueueEmpty => 20,
            Self::StackEmpty => 21,
            Self::OptionalNone => 22,
        }
    }
}

struct Artifact {
    instructions: Vec<Instruction>,
    instruction_byte_ips: Vec<usize>,
    byte_targets: Vec<usize>,
    interface_tables: Vec<Vec<DispatchEntry>>,
    hosts: Vec<HostBinding>,
    field_count: usize,
    strings: Vec<Vec<u8>>,
    types: Vec<TypeInfo>,
    source_paths: Vec<Vec<u8>>,
    debug_entries: Vec<(usize, i32, i32, i32)>,
    callables: Vec<CallableInfo>,
}

struct Vm {
    host_context: i32,
    instructions: Vec<Instruction>,
    instruction_byte_ips: Vec<usize>,
    byte_targets: Vec<usize>,
    interface_tables: Vec<Vec<DispatchEntry>>,
    hosts: Vec<HostBinding>,
    field_count: usize,
    string_pool: Vec<Value>,
    strings: Vec<Vec<u8>>,
    free_strings: Vec<usize>,
    string_free: Vec<u8>,
    types: Vec<TypeInfo>,
    stack: Vec<Value>,
    stack_pointer: usize,
    locals: Vec<Value>,
    globals: Vec<Value>,
    call_frames: Vec<CallFrame>,
    frame_pointer: usize,
    frame_base: usize,
    frame_size: usize,
    locals_top: usize,
    arrays: Vec<Vec<Value>>,
    objects: Vec<Vec<Value>>,
    object_types: Vec<usize>,
    object_initialized: Vec<Vec<u8>>,
    records: Vec<Vec<Value>>,
    record_types: Vec<usize>,
    record_initialized: Vec<Vec<u8>>,
    maps: Vec<Vec<(Value, Value)>>,
    sets: Vec<Vec<Value>>,
    queues: Vec<Vec<Value>>,
    queue_heads: Vec<usize>,
    value_stacks: Vec<Vec<Value>>,
    fallibles: Vec<(bool, Value, Value, Value)>,
    allocations_since_gc: usize,
    garbage_collections: u64,
    profile_enabled: bool,
    profile_instruction_count: u64,
    profile_opcodes: [u64; 256],
    profile_object_allocations: u64,
    profile_array_allocations: u64,
    profile_frame_allocations: u64,
    profile_host_calls: u64,
    profile_stack_high_water: usize,
    profile_locals_high_water: usize,
    profile_call_high_water: usize,
    profile_functions: Vec<FunctionProfile>,
    profile_timing_stack: Vec<TimingFrame>,
    profile_host_calls_by_binding: Vec<u64>,
    profile_host_ms_by_binding: Vec<f64>,
    callables: Vec<CallableInfo>,
    source_paths: Vec<Vec<u8>>,
    debug_entries: Vec<(usize, i32, i32, i32)>,
    current_byte_ip: usize,
    last_error: Option<VmError>,
    output: Value,
}

impl Vm {
    fn new(artifact: Artifact) -> Self {
        let function_count = artifact.callables.len();
        let host_count = artifact.hosts.len();
        let mut strings = Vec::with_capacity(artifact.strings.len());
        let mut string_pool = Vec::with_capacity(artifact.strings.len());
        for text in artifact.strings {
            let handle = strings.len();
            strings.push(text);
            string_pool.push(Value::handle(TAG_STRING, handle));
        }
        let string_free = vec![0; strings.len()];
        Self {
            host_context: 0,
            instructions: artifact.instructions,
            instruction_byte_ips: artifact.instruction_byte_ips,
            byte_targets: artifact.byte_targets,
            interface_tables: artifact.interface_tables,
            hosts: artifact.hosts,
            field_count: artifact.field_count,
            string_pool,
            strings,
            free_strings: Vec::new(),
            string_free,
            types: artifact.types,
            source_paths: artifact.source_paths,
            debug_entries: artifact.debug_entries,
            profile_functions: vec![FunctionProfile::default(); function_count],
            profile_timing_stack: Vec::with_capacity(256),
            profile_host_calls_by_binding: vec![0; host_count],
            profile_host_ms_by_binding: vec![0.0; host_count],
            callables: artifact.callables,
            stack: vec![Value::default(); 4096],
            stack_pointer: 0,
            locals: vec![Value::default(); 16_384],
            globals: vec![Value::default(); 256],
            call_frames: vec![CallFrame::default(); 512],
            frame_pointer: 0,
            frame_base: 0,
            frame_size: ROOT_FRAME_SIZE,
            locals_top: ROOT_FRAME_SIZE,
            arrays: Vec::with_capacity(32),
            objects: Vec::with_capacity(2048),
            object_types: Vec::with_capacity(2048),
            object_initialized: Vec::with_capacity(2048),
            records: Vec::with_capacity(64),
            record_types: Vec::with_capacity(64),
            record_initialized: Vec::with_capacity(64),
            maps: Vec::new(),
            sets: Vec::new(),
            queues: Vec::new(),
            queue_heads: Vec::new(),
            value_stacks: Vec::new(),
            fallibles: Vec::new(),
            allocations_since_gc: 0,
            garbage_collections: 0,
            profile_enabled: false,
            profile_instruction_count: 0,
            profile_opcodes: [0; 256],
            profile_object_allocations: 0,
            profile_array_allocations: 0,
            profile_frame_allocations: 0,
            profile_host_calls: 0,
            profile_stack_high_water: 0,
            profile_locals_high_water: 0,
            profile_call_high_water: 0,
            current_byte_ip: HEADER_SIZE,
            last_error: None,
            output: Value::default(),
        }
    }

    fn allocate_string(&mut self, text: Vec<u8>) -> Value {
        self.allocations_since_gc += 1;
        let handle = if let Some(reused) = self.free_strings.pop() {
            self.strings[reused] = text;
            self.string_free[reused] = 0;
            reused
        } else {
            let next = self.strings.len();
            self.strings.push(text);
            self.string_free.push(0);
            next
        };
        Value::handle(TAG_STRING, handle)
    }

    fn allocate_object(&mut self, type_id: usize, record: bool) -> Result<Value, VmError> {
        let type_info = self.types.get(type_id).ok_or(VmError::Bounds)?;
        if type_info.is_record != record { return Err(VmError::InvalidMetadata); }
        if self.profile_enabled { self.profile_object_allocations += 1; }
        if record {
            self.allocations_since_gc += 1;
            let handle = self.records.len();
            self.records.push(vec![Value::default(); self.field_count.max(1)]);
            self.record_types.push(type_id);
            self.record_initialized.push(vec![0; self.field_count.max(1)]);
            Ok(Value::handle(TAG_RECORD, handle))
        } else {
            self.allocations_since_gc += 1;
            let handle = self.objects.len();
            self.objects.push(vec![Value::default(); self.field_count.max(1)]);
            self.object_types.push(type_id);
            self.object_initialized.push(vec![0; self.field_count.max(1)]);
            Ok(Value::handle(TAG_OBJECT, handle))
        }
    }

    fn reset_profile(&mut self) {
        self.profile_instruction_count = 0;
        self.profile_opcodes.fill(0);
        self.profile_object_allocations = 0;
        self.profile_array_allocations = 0;
        self.profile_frame_allocations = 0;
        self.profile_host_calls = 0;
        self.profile_stack_high_water = self.stack_pointer;
        self.profile_locals_high_water = self.locals_top;
        self.profile_call_high_water = self.frame_pointer;
        self.profile_functions.fill(FunctionProfile::default());
        self.profile_timing_stack.clear();
        self.profile_host_calls_by_binding.fill(0);
        self.profile_host_ms_by_binding.fill(0.0);
    }

    #[inline(always)]
    fn profile_enter_function(&mut self, target: usize) {
        if self.profile_enabled { self.profile_enter_function_enabled(target); }
    }

    #[cold]
    fn profile_enter_function_enabled(&mut self, target: usize) {
        if let Some(function) = self.callables.iter().position(|callable| callable.target == target) {
            self.profile_functions[function].calls += 1;
            self.profile_timing_stack.push(TimingFrame { function, started_ms: host_monotonic_milliseconds(), child_ms: 0.0 });
        }
    }

    #[cold]
    fn profile_leave_function_enabled(&mut self) {
        let Some(frame) = self.profile_timing_stack.pop() else { return; };
        let elapsed = host_monotonic_milliseconds() - frame.started_ms;
        self.profile_functions[frame.function].inclusive_ms += elapsed;
        self.profile_functions[frame.function].self_ms += elapsed - frame.child_ms;
        if let Some(parent) = self.profile_timing_stack.last_mut() { parent.child_ms += elapsed; }
    }

    fn debug_location(&self, byte_ip: usize) -> (i32, i32, i32) {
        if let Some((_, line, column, source_id)) = self.debug_entries.iter().find(|(ip, _, _, _)| *ip == byte_ip) {
            return (*line, *column, *source_id);
        }

        let mut nearest_ip = 0usize;
        let mut nearest = (-1, -1, -1);
        for (ip, line, column, source_id) in &self.debug_entries {
            if *ip <= byte_ip && *ip >= nearest_ip {
                nearest_ip = *ip;
                nearest = (*line, *column, *source_id);
            }
        }
        nearest
    }

    fn value_equals(&self, left: Value, right: Value) -> bool {
        if left.tag != right.tag { return false; }
        match left.tag {
            TAG_NUMBER | TAG_OPTIONAL_NONE => left.payload == right.payload,
            TAG_STRING => self.strings.get(left.payload as usize) == self.strings.get(right.payload as usize),
            TAG_RECORD => {
                let left_handle = left.payload as usize;
                let right_handle = right.payload as usize;
                if self.record_types.get(left_handle) != self.record_types.get(right_handle) { return false; }
                let Some(type_id) = self.record_types.get(left_handle).copied() else { return false; };
                let Some(type_info) = self.types.get(type_id) else { return false; };
                let Some(left_fields) = self.records.get(left_handle) else { return false; };
                let Some(right_fields) = self.records.get(right_handle) else { return false; };
                let Some(left_initialized) = self.record_initialized.get(left_handle) else { return false; };
                let Some(right_initialized) = self.record_initialized.get(right_handle) else { return false; };
                type_info.hash_field_slots.iter().all(|slot| {
                    let slot = *slot;
                    let left_init = left_initialized.get(slot).copied().unwrap_or(0);
                    let right_init = right_initialized.get(slot).copied().unwrap_or(0);
                    left_init == right_init &&
                        (left_init == 0 ||
                            self.value_equals(
                                *left_fields.get(slot).unwrap_or(&Value::default()),
                                *right_fields.get(slot).unwrap_or(&Value::default())))
                })
            }
            TAG_ARRAY => {
                let Some(left_values) = self.arrays.get(left.payload as usize) else { return false; };
                let Some(right_values) = self.arrays.get(right.payload as usize) else { return false; };
                left_values.len() == right_values.len() && left_values.iter().zip(right_values).all(|(a, b)| self.value_equals(*a, *b))
            }
            TAG_QUEUE => {
                let Some(left_values) = self.queues.get(left.payload as usize) else { return false; };
                let Some(right_values) = self.queues.get(right.payload as usize) else { return false; };
                let left_head = self.queue_heads.get(left.payload as usize).copied().unwrap_or(0).min(left_values.len());
                let right_head = self.queue_heads.get(right.payload as usize).copied().unwrap_or(0).min(right_values.len());
                let left_live = &left_values[left_head..];
                let right_live = &right_values[right_head..];
                left_live.len() == right_live.len() && left_live.iter().zip(right_live).all(|(a, b)| self.value_equals(*a, *b))
            }
            TAG_STACK => {
                let Some(left_values) = self.value_stacks.get(left.payload as usize) else { return false; };
                let Some(right_values) = self.value_stacks.get(right.payload as usize) else { return false; };
                left_values.len() == right_values.len() && left_values.iter().rev().zip(right_values.iter().rev()).all(|(a, b)| self.value_equals(*a, *b))
            }
            TAG_SET => {
                let Some(left_values) = self.sets.get(left.payload as usize) else { return false; };
                let Some(right_values) = self.sets.get(right.payload as usize) else { return false; };
                left_values.len() == right_values.len() && left_values.iter().all(|left_value| right_values.iter().any(|right_value| self.value_equals(*left_value, *right_value)))
            }
            TAG_MAP => {
                let Some(left_values) = self.maps.get(left.payload as usize) else { return false; };
                let Some(right_values) = self.maps.get(right.payload as usize) else { return false; };
                left_values.len() == right_values.len() && left_values.iter().all(|(left_key, left_value)| {
                    right_values.iter().any(|(right_key, right_value)| self.value_equals(*left_key, *right_key) && self.value_equals(*left_value, *right_value))
                })
            }
            _ => left.payload == right.payload,
        }
    }

    fn value_text(&self, value: Value) -> Vec<u8> {
        match value.tag {
            TAG_NUMBER => {
                let number = f64::from_bits(value.payload);
                if number.fract() == 0.0 { format!("{}", number as i64).into_bytes() } else { format!("{number}").into_bytes() }
            }
            TAG_STRING => self.strings.get(value.payload as usize).cloned().unwrap_or_default(),
            TAG_OPTIONAL_NONE => b"none".to_vec(),
            TAG_OBJECT => self.object_types.get(value.payload as usize)
                .and_then(|type_id| self.types.get(*type_id)).map(|info| [info.name.as_slice(), b" instance"].concat()).unwrap_or_default(),
            TAG_RECORD => self.record_types.get(value.payload as usize)
                .and_then(|type_id| self.types.get(*type_id)).map(|info| [info.name.as_slice(), b" value"].concat()).unwrap_or_default(),
            _ => b"value".to_vec(),
        }
    }

    fn snapshot_hash_key(&mut self, value: Value) -> Result<Value, VmError> {
        match value.tag {
            TAG_RECORD => {
                let source = value.payload as usize;
                let type_id = *self.record_types.get(source).ok_or(VmError::Bounds)?;
                let fields = self.records.get(source).ok_or(VmError::Bounds)?.clone();
                let initialized = self.record_initialized.get(source).ok_or(VmError::Bounds)?.clone();
                let clone = self.allocate_object(type_id, true)?;
                let target = clone.payload as usize;
                for slot in 0..fields.len() {
                    if initialized.get(slot).copied().unwrap_or(0) == 0 { continue; }
                    let field_value = self.snapshot_hash_key(fields[slot])?;
                    *self.records.get_mut(target).and_then(|record| record.get_mut(slot)).ok_or(VmError::Bounds)? = field_value;
                    *self.record_initialized.get_mut(target).and_then(|record| record.get_mut(slot)).ok_or(VmError::Bounds)? = 1;
                }
                Ok(clone)
            }
            TAG_ARRAY => {
                let source = value.payload as usize;
                let values = self.arrays.get(source).ok_or(VmError::Bounds)?.clone();
                let handle = self.arrays.len();
                self.allocations_since_gc += 1;
                self.arrays.push(Vec::with_capacity(values.len()));
                for item in values {
                    let snapshot = self.snapshot_hash_key(item)?;
                    self.arrays.get_mut(handle).ok_or(VmError::Bounds)?.push(snapshot);
                }
                Ok(Value::handle(TAG_ARRAY, handle))
            }
            TAG_MAP => {
                let source = value.payload as usize;
                let values = self.maps.get(source).ok_or(VmError::Bounds)?.clone();
                let handle = self.maps.len();
                self.allocations_since_gc += 1;
                self.maps.push(Vec::with_capacity(values.len()));
                for (key, entry_value) in values {
                    let snapshot_key = self.snapshot_hash_key(key)?;
                    let snapshot_value = self.snapshot_hash_key(entry_value)?;
                    let position = self.maps.get(handle).ok_or(VmError::Bounds)?.iter().position(|(candidate, _)| self.value_equals(*candidate, snapshot_key));
                    if let Some(index) = position { self.maps[handle][index].1 = snapshot_value; } else { self.maps[handle].push((snapshot_key, snapshot_value)); }
                }
                Ok(Value::handle(TAG_MAP, handle))
            }
            TAG_SET => {
                let source = value.payload as usize;
                let values = self.sets.get(source).ok_or(VmError::Bounds)?.clone();
                let handle = self.sets.len();
                self.allocations_since_gc += 1;
                self.sets.push(Vec::with_capacity(values.len()));
                for item in values {
                    let snapshot = self.snapshot_hash_key(item)?;
                    if !self.sets.get(handle).ok_or(VmError::Bounds)?.iter().any(|candidate| self.value_equals(*candidate, snapshot)) {
                        self.sets.get_mut(handle).ok_or(VmError::Bounds)?.push(snapshot);
                    }
                }
                Ok(Value::handle(TAG_SET, handle))
            }
            TAG_QUEUE => {
                let source = value.payload as usize;
                let values = self.queues.get(source).ok_or(VmError::Bounds)?.clone();
                let head = self.queue_heads.get(source).copied().unwrap_or(0).min(values.len());
                let handle = self.queues.len();
                self.allocations_since_gc += 1;
                self.queues.push(Vec::with_capacity(values.len().saturating_sub(head)));
                self.queue_heads.push(0);
                for item in values[head..].iter().copied() {
                    let snapshot = self.snapshot_hash_key(item)?;
                    self.queues.get_mut(handle).ok_or(VmError::Bounds)?.push(snapshot);
                }
                Ok(Value::handle(TAG_QUEUE, handle))
            }
            TAG_STACK => {
                let source = value.payload as usize;
                let values = self.value_stacks.get(source).ok_or(VmError::Bounds)?.clone();
                let handle = self.value_stacks.len();
                self.allocations_since_gc += 1;
                self.value_stacks.push(Vec::with_capacity(values.len()));
                for item in values {
                    let snapshot = self.snapshot_hash_key(item)?;
                    self.value_stacks.get_mut(handle).ok_or(VmError::Bounds)?.push(snapshot);
                }
                Ok(Value::handle(TAG_STACK, handle))
            }
            _ => Ok(value),
        }
    }

    fn invoke_imported_host(&mut self, binding_id: usize, arguments: &[Value]) -> Result<Value, VmError> {
        let mut result = Value::default();
        let status = imported_host_call(
            self.host_context,
            binding_id as i32,
            arguments.as_ptr(),
            arguments.len(),
            &mut result,
        );
        if status == 0 { Ok(result) } else { Err(VmError::UnsupportedHost) }
    }

    #[inline(always)]
    fn maybe_collect_garbage(&mut self) {
        if self.allocations_since_gc >= 4096 { self.collect_garbage(); }
    }

    #[cold]
    fn collect_garbage(&mut self) {
        let mut marked = HashSet::<(u32, usize)>::new();
        let mut work = Vec::<Value>::new();
        work.extend_from_slice(&self.stack[..self.stack_pointer]);
        work.extend_from_slice(&self.locals[..self.locals_top.min(self.locals.len())]);
        work.extend_from_slice(&self.globals);
        work.extend_from_slice(&self.string_pool);
        work.push(self.output);

        while let Some(value) = work.pop() {
            let handle = value.payload as usize;
            if !matches!(value.tag, TAG_ARRAY | TAG_OBJECT | TAG_STRING | TAG_RECORD | TAG_MAP | TAG_SET | TAG_QUEUE | TAG_STACK | TAG_FALLIBLE) {
                continue;
            }
            if !marked.insert((value.tag, handle)) { continue; }
            match value.tag {
                TAG_ARRAY => if let Some(values) = self.arrays.get(handle) { work.extend_from_slice(values); },
                TAG_OBJECT => if let Some(values) = self.objects.get(handle) { work.extend_from_slice(values); },
                TAG_RECORD => if let Some(values) = self.records.get(handle) { work.extend_from_slice(values); },
                TAG_MAP => if let Some(values) = self.maps.get(handle) { for (key, value) in values { work.push(*key); work.push(*value); } },
                TAG_SET => if let Some(values) = self.sets.get(handle) { work.extend_from_slice(values); },
                TAG_QUEUE => if let Some(values) = self.queues.get(handle) {
                    let head = self.queue_heads.get(handle).copied().unwrap_or(0).min(values.len());
                    work.extend_from_slice(&values[head..]);
                },
                TAG_STACK => if let Some(values) = self.value_stacks.get(handle) { work.extend_from_slice(values); },
                TAG_FALLIBLE => if let Some((_, value, code, message)) = self.fallibles.get(handle) { work.push(*value); work.push(*code); work.push(*message); },
                _ => {}
            }
        }

        for (index, value) in self.strings.iter_mut().enumerate() {
            if !marked.contains(&(TAG_STRING, index)) && self.string_free.get(index).copied().unwrap_or(0) == 0 {
                value.clear();
                value.shrink_to_fit();
                self.string_free[index] = 1;
                self.free_strings.push(index);
            }
        }
        for (index, value) in self.arrays.iter_mut().enumerate() { if !marked.contains(&(TAG_ARRAY, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.objects.iter_mut().enumerate() { if !marked.contains(&(TAG_OBJECT, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.records.iter_mut().enumerate() { if !marked.contains(&(TAG_RECORD, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.maps.iter_mut().enumerate() { if !marked.contains(&(TAG_MAP, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.sets.iter_mut().enumerate() { if !marked.contains(&(TAG_SET, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.queues.iter_mut().enumerate() {
            if !marked.contains(&(TAG_QUEUE, index)) {
                value.clear();
                value.shrink_to_fit();
                if let Some(head) = self.queue_heads.get_mut(index) { *head = 0; }
            }
        }
        for (index, value) in self.value_stacks.iter_mut().enumerate() { if !marked.contains(&(TAG_STACK, index)) { value.clear(); value.shrink_to_fit(); } }
        for (index, value) in self.fallibles.iter_mut().enumerate() {
            if !marked.contains(&(TAG_FALLIBLE, index)) { *value = (false, Value::default(), Value::default(), Value::default()); }
        }
        self.allocations_since_gc = 0;
        self.garbage_collections += 1;
    }

    #[inline(always)]
    fn push(&mut self, value: Value) -> Result<(), VmError> {
        if self.stack_pointer >= self.stack.len() {
            return Err(VmError::Capacity);
        }
        self.stack[self.stack_pointer] = value;
        self.stack_pointer += 1;
        Ok(())
    }

    #[inline(always)]
    fn pop(&mut self) -> Result<Value, VmError> {
        if self.stack_pointer == 0 {
            return Err(VmError::StackUnderflow);
        }
        self.stack_pointer -= 1;
        Ok(self.stack[self.stack_pointer])
    }

    #[inline(always)]
    fn pop_number(&mut self) -> Result<f64, VmError> {
        self.pop()?.number_value()
    }

    #[inline(always)]
    fn pop_handle(&mut self, expected: u32) -> Result<usize, VmError> {
        let value = self.pop()?;
        if value.tag != expected {
            return Err(if expected == TAG_ARRAY { VmError::ExpectedArray } else { VmError::ExpectedObject });
        }
        Ok(value.payload as usize)
    }

    fn run(&mut self) -> Result<(), VmError> { self.run_from(0) }

    fn run_from(&mut self, start_ip: usize) -> Result<(), VmError> {
        if self.profile_enabled { self.run_loop::<true>(start_ip) } else { self.run_loop::<false>(start_ip) }
    }

    fn run_loop<const PROFILE: bool>(&mut self, start_ip: usize) -> Result<(), VmError> {
        self.maybe_collect_garbage();
        let mut ip = start_ip;
        while ip < self.instructions.len() {
            let instruction = self.instructions[ip];
            self.current_byte_ip = self.instruction_byte_ips[ip];
            if PROFILE {
                self.profile_instruction_count += 1;
                self.profile_opcodes[instruction.op as usize] += 1;
                self.profile_stack_high_water = self.profile_stack_high_water.max(self.stack_pointer);
                self.profile_locals_high_water = self.profile_locals_high_water.max(self.locals_top);
                self.profile_call_high_water = self.profile_call_high_water.max(self.frame_pointer);
            }
            ip += 1;
            match instruction.op {
                0x01 => self.push(Value::number(instruction.a as f64))?,
                0x02 => {
                    let right = self.pop()?;
                    let left = self.pop()?;
                    if left.tag == TAG_STRING || right.tag == TAG_STRING {
                        let mut text = self.value_text(left);
                        text.extend_from_slice(&self.value_text(right));
                        let value = self.allocate_string(text);
                        self.push(value)?;
                    } else {
                        self.push(Value::number(left.number_value()? + right.number_value()?))?;
                    }
                }
                0x03 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    self.push(Value::number(left - right))?;
                }
                0x04 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    self.push(Value::number(left * right))?;
                }
                0x05 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    if right == 0.0 { return Err(VmError::DivisionByZero); }
                    self.push(Value::number(left / right))?;
                }
                0x06 => {
                    self.output = self.pop()?;
                    imported_output(self.host_context, &self.output);
                }
                0x07 => {
                    if self.stack_pointer == 0 { return Err(VmError::StackUnderflow); }
                    self.push(self.stack[self.stack_pointer - 1])?;
                }
                0x08 => {
                    let right = self.pop()?;
                    let left = self.pop()?;
                    self.push(right)?;
                    self.push(left)?;
                }
                0x09 => { self.pop()?; }
                0x0A => ip = instruction.a as usize,
                0x0B => {
                    if self.pop_number()? == 0.0 { ip = instruction.a as usize; }
                }
                0x0C => {
                    if self.pop_number()? != 0.0 { ip = instruction.a as usize; }
                }
                0x0D => {
                    let index = self.frame_base + instruction.a as usize;
                    let value = *self.locals.get(index).ok_or(VmError::Bounds)?;
                    self.push(value)?;
                }
                0x0E => {
                    let index = self.frame_base + instruction.a as usize;
                    let value = self.pop()?;
                    *self.locals.get_mut(index).ok_or(VmError::Bounds)? = value;
                    if self.frame_pointer == 0 && instruction.a as usize >= self.frame_size {
                        self.frame_size = instruction.a as usize + 1;
                        self.locals_top = self.frame_size;
                    }
                }
                0x0F => {
                    let right = self.pop()?;
                    let left = self.pop()?;
                    let equal = self.value_equals(left, right);
                    self.push(Value::number(if equal { 1.0 } else { 0.0 }))?;
                }
                0x10 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    self.push(Value::number(if left < right { 1.0 } else { 0.0 }))?;
                }
                0x11 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    self.push(Value::number(if left > right { 1.0 } else { 0.0 }))?;
                }
                0x12 => {
                    if PROFILE { self.profile_frame_allocations += 1; }
                    let argument_count = instruction.b as usize;
                    let new_frame_size = (instruction.c as usize).max(argument_count).max(1);
                    let new_frame_base = self.locals_top;
                    let new_top = new_frame_base.checked_add(new_frame_size).ok_or(VmError::Capacity)?;
                    if new_top > self.locals.len() || self.frame_pointer >= self.call_frames.len() {
                        return Err(VmError::Capacity);
                    }
                    self.locals[new_frame_base..new_top].fill(Value::default());
                    for argument in (0..argument_count).rev() {
                        self.locals[new_frame_base + argument] = self.pop()?;
                    }
                    self.call_frames[self.frame_pointer] = CallFrame {
                        return_ip: ip,
                        call_byte_ip: self.current_byte_ip,
                        frame_base: self.frame_base,
                        frame_size: self.frame_size,
                        locals_top: self.locals_top,
                    };
                    self.frame_pointer += 1;
                    self.frame_base = new_frame_base;
                    self.frame_size = new_frame_size;
                    self.locals_top = new_top;
                    ip = instruction.a as usize;
                    if PROFILE { self.profile_enter_function_enabled(ip); }
                }
                0x13 => {
                    let result = self.pop()?;
                    if PROFILE { self.profile_leave_function_enabled(); }
                    if self.frame_pointer == 0 { return Ok(()); }
                    self.frame_pointer -= 1;
                    let frame = self.call_frames[self.frame_pointer];
                    ip = frame.return_ip;
                    self.frame_base = frame.frame_base;
                    self.frame_size = frame.frame_size;
                    self.locals_top = frame.locals_top;
                    self.push(result)?;
                }
                0x14 => {
                    let value = *self.string_pool.get(instruction.a as usize).ok_or(VmError::Bounds)?;
                    self.push(value)?;
                }
                0x15 => { self.pop()?; return Err(VmError::UserError); }
                0x16 => {
                    let count = instruction.a as usize;
                    if count > self.stack_pointer { return Err(VmError::StackUnderflow); }
                    let start = self.stack_pointer - count;
                    let items = self.stack[start..self.stack_pointer].to_vec();
                    self.stack_pointer = start;
                    let handle = self.arrays.len();
                    if PROFILE { self.profile_array_allocations += 1; }
                    self.allocations_since_gc += 1;
                    self.arrays.push(items);
                    self.push(Value::handle(TAG_ARRAY, handle))?;
                }
                0x17 => {
                    let collection = self.pop()?;
                    let handle = collection.payload as usize;
                    let length = match collection.tag {
                        TAG_ARRAY => self.arrays.get(handle).map(Vec::len),
                        TAG_MAP => self.maps.get(handle).map(Vec::len),
                        TAG_SET => self.sets.get(handle).map(Vec::len),
                        TAG_QUEUE => self.queues.get(handle).zip(self.queue_heads.get(handle)).map(|(items, head)| items.len() - head),
                        TAG_STACK => self.value_stacks.get(handle).map(Vec::len),
                        _ => None,
                    }.ok_or(VmError::Bounds)?;
                    self.push(Value::number(length as f64))?;
                }
                0x18 => {
                    let index = self.pop_number()? as usize;
                    let handle = self.pop_handle(TAG_ARRAY)?;
                    let value = *self.arrays.get(handle).and_then(|array| array.get(index)).ok_or(VmError::ArrayIndexOutOfRange)?;
                    self.push(value)?;
                }
                0x19 => {
                    let raw_size = self.pop_number()?;
                    if raw_size < 0.0 { return Err(VmError::Bounds); }
                    let size = raw_size.trunc() as usize;
                    let handle = self.arrays.len();
                    if PROFILE { self.profile_array_allocations += 1; }
                    self.allocations_since_gc += 1;
                    self.arrays.push(vec![Value::default(); size]);
                    self.push(Value::handle(TAG_ARRAY, handle))?;
                }
                0x1A => self.push(Value::handle(TAG_OPTIONAL_NONE, 0))?,
                0x1B => {
                    let value = self.pop()?;
                    self.push(Value::number(if value.tag == TAG_OPTIONAL_NONE { 0.0 } else { 1.0 }))?;
                }
                0x1C => {
                    let value = self.pop()?;
                    if value.tag == TAG_OPTIONAL_NONE { return Err(VmError::OptionalNone); }
                    self.push(value)?;
                }
                0x1D => {
                    let fallback = self.pop()?;
                    let value = self.pop()?;
                    self.push(if value.tag == TAG_OPTIONAL_NONE { fallback } else { value })?;
                }
                0x1E => {
                    let value = self.pop()?;
                    let index = self.pop_number()? as usize;
                    let handle = self.pop_handle(TAG_ARRAY)?;
                    *self.arrays.get_mut(handle).and_then(|array| array.get_mut(index)).ok_or(VmError::ArrayIndexOutOfRange)? = value;
                    self.push(value)?;
                }
                0x1F => {
                    let value = self.allocate_object(instruction.a as usize, false)?;
                    self.push(value)?;
                }
                0x20 => {
                    let target = self.pop()?;
                    let handle = target.payload as usize;
                    let slot = instruction.a as usize;
                    let (fields, initialized) = match target.tag {
                        TAG_OBJECT => (self.objects.get(handle), self.object_initialized.get(handle)),
                        TAG_RECORD => (self.records.get(handle), self.record_initialized.get(handle)),
                        _ => return Err(VmError::ExpectedObject),
                    };
                    if initialized.and_then(|value| value.get(slot)).copied() != Some(1) { return Err(VmError::Bounds); }
                    let value = *fields.and_then(|object| object.get(slot)).ok_or(VmError::Bounds)?;
                    self.push(value)?;
                }
                0x21 => {
                    let value = self.pop()?;
                    let target = self.pop()?;
                    let handle = target.payload as usize;
                    let slot = instruction.a as usize;
                    match target.tag {
                        TAG_OBJECT => {
                            *self.objects.get_mut(handle).and_then(|object| object.get_mut(slot)).ok_or(VmError::Bounds)? = value;
                            *self.object_initialized.get_mut(handle).and_then(|object| object.get_mut(slot)).ok_or(VmError::Bounds)? = 1;
                        }
                        TAG_RECORD => {
                            *self.records.get_mut(handle).and_then(|object| object.get_mut(slot)).ok_or(VmError::Bounds)? = value;
                            *self.record_initialized.get_mut(handle).and_then(|object| object.get_mut(slot)).ok_or(VmError::Bounds)? = 1;
                        }
                        _ => return Err(VmError::ExpectedObject),
                    }
                    self.push(value)?;
                }
                0x22 => {
                    let target = self.pop()?;
                    let type_id = match target.tag {
                        TAG_OBJECT => *self.object_types.get(target.payload as usize).ok_or(VmError::Bounds)?,
                        TAG_RECORD => *self.record_types.get(target.payload as usize).ok_or(VmError::Bounds)?,
                        _ => return Err(VmError::ExpectedObject),
                    };
                    let name = self.types.get(type_id).ok_or(VmError::Bounds)?.name.clone();
                    let value = self.allocate_string(name);
                    self.push(value)?;
                }
                0x23 => {
                    if PROFILE { self.profile_frame_allocations += 1; }
                    let explicit_count = instruction.a as usize;
                    if self.stack_pointer < explicit_count + 1 { return Err(VmError::StackUnderflow); }
                    let argument_start = self.stack_pointer - explicit_count;
                    let arguments = self.stack[argument_start..self.stack_pointer].to_vec();
                    self.stack_pointer = argument_start;
                    let target = self.pop()?;
                    let type_id = match target.tag {
                        TAG_OBJECT => *self.object_types.get(target.payload as usize).ok_or(VmError::Bounds)?,
                        TAG_RECORD => *self.record_types.get(target.payload as usize).ok_or(VmError::Bounds)?,
                        _ => return Err(VmError::ExpectedObject),
                    };
                    let entry = *self.interface_tables.get(instruction.b as usize).ok_or(VmError::Bounds)?
                        .iter().find(|entry| entry.type_id == type_id).ok_or(VmError::Bounds)?;
                    let total_count = explicit_count + 1;
                    let new_frame_size = entry.frame_size.max(total_count).max(1);
                    let new_frame_base = self.locals_top;
                    let new_top = new_frame_base.checked_add(new_frame_size).ok_or(VmError::Capacity)?;
                    if new_top > self.locals.len() || self.frame_pointer >= self.call_frames.len() { return Err(VmError::Capacity); }
                    self.locals[new_frame_base..new_top].fill(Value::default());
                    self.locals[new_frame_base] = target;
                    self.locals[new_frame_base + 1..new_frame_base + total_count].copy_from_slice(&arguments);
                    self.call_frames[self.frame_pointer] = CallFrame { return_ip: ip, call_byte_ip: self.current_byte_ip, frame_base: self.frame_base, frame_size: self.frame_size, locals_top: self.locals_top };
                    self.frame_pointer += 1;
                    self.frame_base = new_frame_base;
                    self.frame_size = new_frame_size;
                    self.locals_top = new_top;
                    ip = entry.target;
                    if PROFILE { self.profile_enter_function_enabled(ip); }
                }
                0x24 => {
                    let right = self.pop_number()?;
                    let left = self.pop_number()?;
                    if right == 0.0 { return Err(VmError::ModuloByZero); }
                    self.push(Value::number(left % right))?;
                }
                0x25 => self.push(Value::number(host_unix_milliseconds().trunc()))?,
                0x26 => self.push(Value::number((host_unix_milliseconds() * 1000.0).trunc()))?,
                0x27 => self.push(Value::number((host_monotonic_milliseconds() * 1_000_000.0).trunc()))?,
                0x28 => self.push(Value::number((host_monotonic_milliseconds() * 1000.0).trunc()))?,
                0x29 => self.push(Value::number(1_000_000.0))?,
                0x2A => {
                    if PROFILE { self.profile_host_calls += 1; }
                    let profile_started = if PROFILE { host_monotonic_milliseconds() } else { 0.0 };
                    let binding = self.hosts.get(instruction.a as usize).ok_or(VmError::Bounds)?;
                    let binding_kind = binding.kind;
                    let binding_arity = binding.arity;
                    if binding_arity > self.stack_pointer { return Err(VmError::StackUnderflow); }
                    let result = match binding_kind {
                        HOST_SQUARE_ROOT => Value::number(self.pop_number()?.sqrt()),
                        HOST_PRINT => { self.output = self.pop()?; imported_output(self.host_context, &self.output); Value::number(0.0) }
                        _ => {
                            let start = self.stack_pointer - binding_arity;
                            let arguments = self.stack[start..self.stack_pointer].to_vec();
                            self.stack_pointer = start;
                            self.invoke_imported_host(instruction.a as usize, &arguments)?
                        }
                    };
                    if PROFILE {
                        self.profile_host_calls_by_binding[instruction.a as usize] += 1;
                        self.profile_host_ms_by_binding[instruction.a as usize] += host_monotonic_milliseconds() - profile_started;
                    }
                    self.push(result)?;
                }
                0x2B => {
                    let value = self.pop()?; let handle = self.pop_handle(TAG_ARRAY)?;
                    self.arrays.get_mut(handle).ok_or(VmError::Bounds)?.push(value); self.push(Value::number(0.0))?;
                }
                0x2C => {
                    let index = self.pop_number()?.trunc() as usize; let handle = self.pop_handle(TAG_ARRAY)?;
                    let array = self.arrays.get_mut(handle).ok_or(VmError::Bounds)?;
                    if index >= array.len() { return Err(VmError::ArrayIndexOutOfRange); }
                    array.remove(index); self.push(Value::number(0.0))?;
                }
                0x2D => { let handle = self.maps.len(); self.allocations_since_gc += 1; self.maps.push(Vec::new()); self.push(Value::handle(TAG_MAP, handle))?; }
                0x2E => {
                    let key = self.pop()?; let map_value = self.pop()?;
                    if map_value.tag != TAG_MAP { return Err(VmError::Bounds); }
                    let value = self.maps.get(map_value.payload as usize).ok_or(VmError::Bounds)?.iter()
                        .find(|(candidate, _)| self.value_equals(*candidate, key)).map(|(_, value)| *value).ok_or(VmError::MapKeyNotFound)?;
                    self.push(value)?;
                }
                0x2F => {
                    let value = self.pop()?; let key = self.pop()?; let map_value = self.pop()?;
                    if map_value.tag != TAG_MAP { return Err(VmError::Bounds); }
                    let handle = map_value.payload as usize;
                    let snapshot_key = self.snapshot_hash_key(key)?;
                    let position = self.maps.get(handle).ok_or(VmError::Bounds)?.iter().position(|(candidate, _)| self.value_equals(*candidate, snapshot_key));
                    if let Some(index) = position { self.maps[handle][index].1 = value; } else { self.maps[handle].push((snapshot_key, value)); }
                    self.push(value)?;
                }
                0x30 => {
                    let key = self.pop()?; let map_value = self.pop()?;
                    if map_value.tag != TAG_MAP { return Err(VmError::Bounds); }
                    let found = self.maps.get(map_value.payload as usize).ok_or(VmError::Bounds)?.iter().any(|(candidate, _)| self.value_equals(*candidate, key));
                    self.push(Value::number(if found { 1.0 } else { 0.0 }))?;
                }
                0x31 => {
                    let key = self.pop()?; let map_value = self.pop()?;
                    if map_value.tag != TAG_MAP { return Err(VmError::Bounds); }
                    let handle = map_value.payload as usize;
                    let position = self.maps.get(handle).ok_or(VmError::Bounds)?.iter().position(|(candidate, _)| self.value_equals(*candidate, key));
                    if let Some(index) = position { self.maps[handle].remove(index); }
                    self.push(Value::number(0.0))?;
                }
                0x32 => { let handle = self.sets.len(); self.allocations_since_gc += 1; self.sets.push(Vec::new()); self.push(Value::handle(TAG_SET, handle))?; }
                0x33 => {
                    let value = self.pop()?; let set_value = self.pop()?;
                    if set_value.tag != TAG_SET { return Err(VmError::Bounds); }
                    let handle = set_value.payload as usize;
                    let snapshot = self.snapshot_hash_key(value)?;
                    if !self.sets.get(handle).ok_or(VmError::Bounds)?.iter().any(|candidate| self.value_equals(*candidate, snapshot)) { self.sets[handle].push(snapshot); }
                    self.push(Value::number(0.0))?;
                }
                0x34 => {
                    let value = self.pop()?; let set_value = self.pop()?;
                    if set_value.tag != TAG_SET { return Err(VmError::Bounds); }
                    let found = self.sets.get(set_value.payload as usize).ok_or(VmError::Bounds)?.iter().any(|candidate| self.value_equals(*candidate, value));
                    self.push(Value::number(if found { 1.0 } else { 0.0 }))?;
                }
                0x35 => {
                    let value = self.pop()?; let set_value = self.pop()?;
                    if set_value.tag != TAG_SET { return Err(VmError::Bounds); }
                    let handle = set_value.payload as usize;
                    let position = self.sets.get(handle).ok_or(VmError::Bounds)?.iter().position(|candidate| self.value_equals(*candidate, value));
                    if let Some(index) = position { self.sets[handle].remove(index); }
                    self.push(Value::number(0.0))?;
                }
                0x36 => { let handle = self.queues.len(); self.allocations_since_gc += 1; self.queues.push(Vec::new()); self.queue_heads.push(0); self.push(Value::handle(TAG_QUEUE, handle))?; }
                0x37 => {
                    let value = self.pop()?; let queue = self.pop()?;
                    if queue.tag != TAG_QUEUE { return Err(VmError::Bounds); }
                    self.queues.get_mut(queue.payload as usize).ok_or(VmError::Bounds)?.push(value); self.push(Value::number(0.0))?;
                }
                0x38 | 0x39 => {
                    let queue = self.pop()?;
                    if queue.tag != TAG_QUEUE { return Err(VmError::Bounds); }
                    let handle = queue.payload as usize; let head = *self.queue_heads.get(handle).ok_or(VmError::Bounds)?;
                    let value = *self.queues.get(handle).and_then(|items| items.get(head)).ok_or(VmError::QueueEmpty)?;
                    if instruction.op == 0x38 { self.queue_heads[handle] += 1; }
                    self.push(value)?;
                }
                0x3A => { let handle = self.value_stacks.len(); self.allocations_since_gc += 1; self.value_stacks.push(Vec::new()); self.push(Value::handle(TAG_STACK, handle))?; }
                0x3B => {
                    let value = self.pop()?; let stack = self.pop()?;
                    if stack.tag != TAG_STACK { return Err(VmError::Bounds); }
                    self.value_stacks.get_mut(stack.payload as usize).ok_or(VmError::Bounds)?.push(value); self.push(Value::number(0.0))?;
                }
                0x3C | 0x3D => {
                    let stack = self.pop()?;
                    if stack.tag != TAG_STACK { return Err(VmError::Bounds); }
                    let values = self.value_stacks.get_mut(stack.payload as usize).ok_or(VmError::Bounds)?;
                    let value = if instruction.op == 0x3C { values.pop() } else { values.last().copied() }.ok_or(VmError::StackEmpty)?;
                    self.push(value)?;
                }
                0x3E => { let value = self.allocate_object(instruction.a as usize, true)?; self.push(value)?; }
                0x3F => {
                    let value = self.pop()?; let handle = self.fallibles.len();
                    let empty_message = self.allocate_string(Vec::new());
                    self.allocations_since_gc += 1;
                    self.fallibles.push((false, value, Value::default(), empty_message));
                    self.push(Value::handle(TAG_FALLIBLE, handle))?;
                }
                0x40 => {
                    let message_value = self.pop()?; let code = self.pop()?;
                    let message = if message_value.tag == TAG_STRING { message_value } else { let text = self.value_text(message_value); self.allocate_string(text) };
                    let handle = self.fallibles.len();
                    self.allocations_since_gc += 1;
                    self.fallibles.push((true, Value::default(), code, message));
                    self.push(Value::handle(TAG_FALLIBLE, handle))?;
                }
                0x41 => {
                    let value = self.pop()?;
                    if value.tag != TAG_FALLIBLE { return Err(VmError::Bounds); }
                    let is_error = self.fallibles.get(value.payload as usize).ok_or(VmError::Bounds)?.0;
                    self.push(Value::number(if is_error { 1.0 } else { 0.0 }))?;
                }
                0x42 => {
                    let value = self.pop()?;
                    if value.tag != TAG_FALLIBLE { return Err(VmError::Bounds); }
                    let fallible = *self.fallibles.get(value.payload as usize).ok_or(VmError::Bounds)?;
                    if fallible.0 { return Err(VmError::Bounds); }
                    self.push(fallible.1)?;
                }
                0x43 => {
                    let value = self.pop()?;
                    if value.tag != TAG_FALLIBLE { return Err(VmError::Bounds); }
                    let fallible = *self.fallibles.get(value.payload as usize).ok_or(VmError::Bounds)?;
                    if !fallible.0 { return Err(VmError::Bounds); }
                    self.push(fallible.2)?;
                }
                0x44 => {
                    let value = self.pop()?;
                    if value.tag != TAG_FALLIBLE { return Err(VmError::Bounds); }
                    let fallible = *self.fallibles.get(value.payload as usize).ok_or(VmError::Bounds)?;
                    if !fallible.0 { return Err(VmError::Bounds); }
                    self.push(fallible.3)?;
                }
                0x45 => self.push(Value::number(f64::from_bits(instruction.real_bits)))?,
                0x46 | 0x47 => {
                    let value = self.pop_number()?;
                    if !value.is_finite() { return Err(VmError::Bounds); }
                    let truncated = value.trunc();
                    if (instruction.op == 0x47 && truncated < 0.0) || !(-2_147_483_648.0..=2_147_483_647.0).contains(&truncated) { return Err(VmError::Bounds); }
                    self.push(Value::number(truncated))?;
                }
                0x48 => { let value = self.pop_number()?; self.push(Value::number(value))?; }
                0x49 => {
                    let right = self.pop_number()?.trunc(); let left = self.pop_number()?.trunc();
                    if right == 0.0 { return Err(VmError::DivisionByZero); }
                    self.push(Value::number((left / right).trunc()))?;
                }
                0x4A => self.push(Value::number(i64::from_le_bytes(instruction.real_bits.to_le_bytes()) as f64))?,
                0x4B => {
                    let value = self.pop_number()?;
                    if !value.is_finite() { return Err(VmError::Bounds); }
                    let checked = match instruction.a {
                        1 => checked_integer(value, -128.0, 127.0)?,
                        2 => checked_integer(value, -32768.0, 32767.0)?,
                        3 => checked_integer(value, -2147483648.0, 2147483647.0)?,
                        4 => checked_integer(value, 0.0, 255.0)?,
                        5 => checked_integer(value, 0.0, 65535.0)?,
                        6 => checked_integer(value, 0.0, 4294967295.0)?,
                        7 => {
                            let rounded = value as f32;
                            if !rounded.is_finite() { return Err(VmError::Bounds); }
                            rounded as f64
                        }
                        _ => return Err(VmError::Bounds),
                    };
                    self.push(Value::number(checked))?;
                }
                0x4C => {
                    let value = *self.globals.get(instruction.a as usize).ok_or(VmError::Bounds)?;
                    self.push(value)?;
                }
                0x4D => {
                    let value = self.pop()?;
                    *self.globals.get_mut(instruction.a as usize).ok_or(VmError::Bounds)? = value;
                }
                0xFF => return Ok(()),
                _ => return Err(VmError::UnsupportedOpcode),
            }
        }
        Ok(())
    }
}

fn parse(bytes: &[u8]) -> Result<Artifact, VmError> {
    if bytes.len() < HEADER_SIZE { return Err(VmError::Truncated); }
    if &bytes[0..4] != b"CODE" { return Err(VmError::InvalidArtifact); }
    if bytes[4] != BYTECODE_VERSION { return Err(VmError::UnsupportedVersion); }
    let code_size = read_i32(bytes, 5)?;
    let debug_count = read_i32(bytes, 9)?;
    if code_size < 0 || debug_count < 0 { return Err(VmError::InvalidArtifact); }
    let code_end = HEADER_SIZE.checked_add(code_size as usize).ok_or(VmError::Truncated)?;
    let metadata_offset = code_end
        .checked_add((debug_count as usize).checked_mul(16).ok_or(VmError::Truncated)?)
        .ok_or(VmError::Truncated)?;
    if metadata_offset > bytes.len() { return Err(VmError::Truncated); }
    let mut debug_entries = Vec::with_capacity(debug_count as usize);
    let mut debug_offset = code_end;
    for _ in 0..debug_count {
        debug_entries.push((
            read_i32(bytes, debug_offset)? as usize,
            read_i32(bytes, debug_offset + 4)?,
            read_i32(bytes, debug_offset + 8)?,
            read_i32(bytes, debug_offset + 12)?,
        ));
        debug_offset += 16;
    }
    let metadata = parse_metadata(bytes, metadata_offset)?;
    let (instructions, instruction_byte_ips, byte_targets, interface_tables) = decode(bytes, code_end)?;
    let mut callables = metadata.callables;
    for callable in &mut callables {
        callable.target = byte_targets.get(callable.target).copied().unwrap_or(usize::MAX);
        if callable.target == usize::MAX { return Err(VmError::InvalidTarget); }
    }
    Ok(Artifact {
        instructions,
        instruction_byte_ips,
        byte_targets,
        interface_tables,
        hosts: metadata.hosts,
        field_count: metadata.field_count,
        strings: metadata.strings,
        source_paths: metadata.source_paths,
        types: metadata.types,
        debug_entries,
        callables,
    })
}

fn decode(bytes: &[u8], code_end: usize) -> Result<(Vec<Instruction>, Vec<usize>, Vec<usize>, Vec<Vec<DispatchEntry>>), VmError> {
    if code_end > bytes.len() { return Err(VmError::Truncated); }
    let mut instructions = Vec::with_capacity((code_end - HEADER_SIZE) / 3);
    let mut byte_to_instruction = vec![usize::MAX; code_end + 1];
    let mut interface_tables = Vec::new();
    let mut instruction_byte_ips = Vec::new();
    let mut offset = HEADER_SIZE;
    while offset < code_end {
        let byte_ip = offset;
        let op = bytes[offset];
        offset += 1;
        let mut instruction = Instruction { op, ..Instruction::default() };
        match op {
            0x01 | 0x0A | 0x0B | 0x0C | 0x0D | 0x0E | 0x14 | 0x16 | 0x1F | 0x20 | 0x21 | 0x2A | 0x3E | 0x4C | 0x4D => {
                instruction.a = read_i32_bounded(bytes, offset, code_end)?;
                offset += 4;
            }
            0x12 => {
                instruction.a = read_i32_bounded(bytes, offset, code_end)?;
                instruction.b = read_i32_bounded(bytes, offset + 4, code_end)?;
                instruction.c = read_i32_bounded(bytes, offset + 8, code_end)?;
                offset += 12;
            }
            0x23 => {
                instruction.a = read_i32_bounded(bytes, offset, code_end)?;
                instruction.b = read_i32_bounded(bytes, offset + 4, code_end)?;
                if instruction.b < 0 { return Err(VmError::InvalidArtifact); }
                offset += 8;
                let mut entries = Vec::with_capacity(instruction.b as usize);
                for _ in 0..instruction.b {
                    entries.push(DispatchEntry {
                        type_id: read_i32_bounded(bytes, offset, code_end)? as usize,
                        target: read_i32_bounded(bytes, offset + 4, code_end)? as usize,
                        frame_size: read_i32_bounded(bytes, offset + 8, code_end)? as usize,
                    });
                    offset += 12;
                }
                instruction.b = interface_tables.len() as i32;
                interface_tables.push(entries);
            }
            0x45 => {
                instruction.real_bits = read_u64_bounded(bytes, offset, code_end)?;
                offset += 8;
            }
            0x4A => {
                instruction.real_bits = read_u64_bounded(bytes, offset, code_end)?;
                offset += 8;
            }
            0x4B => {
                if offset >= code_end { return Err(VmError::Truncated); }
                instruction.a = bytes[offset] as i32;
                offset += 1;
            }
            _ => {}
        }
        byte_to_instruction[byte_ip] = instructions.len();
        instruction_byte_ips.push(byte_ip);
        instructions.push(instruction);
    }
    if offset != code_end { return Err(VmError::Truncated); }
    for instruction in &mut instructions {
        if matches!(instruction.op, 0x0A | 0x0B | 0x0C | 0x12) {
            if instruction.a < 0 { return Err(VmError::InvalidTarget); }
            let target = byte_to_instruction.get(instruction.a as usize).copied().unwrap_or(usize::MAX);
            if target == usize::MAX { return Err(VmError::InvalidTarget); }
            instruction.a = target as i32;
        }
    }
    for table in &mut interface_tables {
        for entry in table {
            let target = byte_to_instruction.get(entry.target).copied().unwrap_or(usize::MAX);
            if target == usize::MAX { return Err(VmError::InvalidTarget); }
            entry.target = target;
        }
    }
    Ok((instructions, instruction_byte_ips, byte_to_instruction, interface_tables))
}

struct ParsedMetadata {
    strings: Vec<Vec<u8>>,
    source_paths: Vec<Vec<u8>>,
    hosts: Vec<HostBinding>,
    field_count: usize,
    types: Vec<TypeInfo>,
    callables: Vec<CallableInfo>,
}

fn parse_metadata(bytes: &[u8], offset: usize) -> Result<ParsedMetadata, VmError> {
    if offset + 8 > bytes.len() { return Err(VmError::MissingMetadata); }
    if &bytes[offset..offset + 4] != b"META" { return Err(VmError::MissingMetadata); }
    let payload_size = read_i32(bytes, offset + 4)?;
    if payload_size < 0 || offset + 8 + payload_size as usize != bytes.len() {
        return Err(VmError::InvalidMetadata);
    }
    let mut reader = Reader::new(&bytes[offset + 8..]);
    let string_count = reader.count()?;
    let mut strings = Vec::with_capacity(string_count);
    for _ in 0..string_count { strings.push(reader.string()?); }
    let source_count = reader.count()?;
    let mut source_paths = Vec::with_capacity(source_count);
    for _ in 0..source_count {
        let index = reader.count()?;
        source_paths.push(strings.get(index).ok_or(VmError::InvalidMetadata)?.clone());
    }
    let field_count = reader.count()?;
    for _ in 0..field_count {
        let index = reader.count()?;
        if index >= strings.len() { return Err(VmError::InvalidMetadata); }
    }
    let host_count = reader.count()?;
    let mut hosts = Vec::with_capacity(host_count);
    for _ in 0..host_count {
        let symbol = reader.count()?;
        let _arity = reader.count()?;
        let symbol = strings.get(symbol).ok_or(VmError::InvalidMetadata)?;
        let kind = match symbol.as_slice() {
            b"standard.input_output.print" => HOST_PRINT,
            b"std.math.square_root" => HOST_SQUARE_ROOT,
            _ => HOST_UNSUPPORTED,
        };
        hosts.push(HostBinding { kind, arity: _arity, symbol: symbol.clone() });
    }
    let type_count = reader.count()?;
    let mut types = Vec::with_capacity(type_count);
    for _ in 0..type_count {
        let name = reader.count()?;
        if name >= strings.len() { return Err(VmError::InvalidMetadata); }
        let kind = reader.byte()?;
        if kind > 1 { return Err(VmError::InvalidMetadata); }
        let declared = reader.count()?;
        for _ in 0..declared {
            if reader.count()? >= field_count { return Err(VmError::InvalidMetadata); }
        }
        let hash_field_count = reader.count()?;
        let mut hash_field_slots = Vec::with_capacity(hash_field_count);
        for _ in 0..hash_field_count {
            let slot = reader.count()?;
            if slot >= field_count { return Err(VmError::InvalidMetadata); }
            hash_field_slots.push(slot);
        }
        types.push(TypeInfo { name: strings[name].clone(), is_record: kind == 1, hash_field_slots });
    }
    let callable_count = reader.count()?;
    let mut callables = Vec::with_capacity(callable_count);
    for _ in 0..callable_count {
        let target = reader.count()?;
        let _frame_size = reader.count()?;
        let name = reader.count()?;
        if target < HEADER_SIZE || target >= offset || name >= strings.len() {
            return Err(VmError::InvalidMetadata);
        }
        callables.push(CallableInfo { target, name: strings[name].clone() });
    }
    if !reader.at_end() { return Err(VmError::InvalidMetadata); }
    Ok(ParsedMetadata { strings, source_paths, hosts, field_count, types, callables })
}

struct Reader<'a> {
    bytes: &'a [u8],
    offset: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self { Self { bytes, offset: 0 } }
    fn byte(&mut self) -> Result<u8, VmError> {
        let value = *self.bytes.get(self.offset).ok_or(VmError::Truncated)?;
        self.offset += 1;
        Ok(value)
    }
    fn count(&mut self) -> Result<usize, VmError> {
        let value = read_i32(self.bytes, self.offset)?;
        self.offset += 4;
        if value < 0 { return Err(VmError::InvalidMetadata); }
        Ok(value as usize)
    }
    fn string(&mut self) -> Result<Vec<u8>, VmError> {
        let length = self.count()?;
        let end = self.offset.checked_add(length).ok_or(VmError::Truncated)?;
        let value = self.bytes.get(self.offset..end).ok_or(VmError::Truncated)?.to_vec();
        self.offset = end;
        Ok(value)
    }
    fn at_end(&self) -> bool { self.offset == self.bytes.len() }
}

fn read_i32(bytes: &[u8], offset: usize) -> Result<i32, VmError> {
    let data: [u8; 4] = bytes.get(offset..offset + 4).ok_or(VmError::Truncated)?.try_into().unwrap();
    Ok(i32::from_le_bytes(data))
}

fn checked_integer(value: f64, minimum: f64, maximum: f64) -> Result<f64, VmError> {
    let value = value.trunc();
    if value < minimum || value > maximum { Err(VmError::Bounds) } else { Ok(value) }
}

fn read_i32_bounded(bytes: &[u8], offset: usize, end: usize) -> Result<i32, VmError> {
    if offset + 4 > end { return Err(VmError::Truncated); }
    read_i32(bytes, offset)
}

fn read_u64_bounded(bytes: &[u8], offset: usize, end: usize) -> Result<u64, VmError> {
    if offset + 8 > end { return Err(VmError::Truncated); }
    let data: [u8; 8] = bytes.get(offset..offset + 8).ok_or(VmError::Truncated)?.try_into().unwrap();
    Ok(u64::from_le_bytes(data))
}

#[cfg(target_arch = "wasm32")]
#[link(wasm_import_module = "code_host")]
extern "C" {
    fn call(context: i32, binding_id: i32, arguments: *const Value, argument_count: usize, result: *mut Value) -> i32;
    fn output(context: i32, value: *const Value);
    fn unix_milliseconds() -> f64;
    fn monotonic_milliseconds() -> f64;
}

#[inline]
fn imported_host_call(context: i32, binding_id: i32, arguments: *const Value, argument_count: usize, result: *mut Value) -> i32 {
    #[cfg(target_arch = "wasm32")]
    unsafe { call(context, binding_id, arguments, argument_count, result) }
    #[cfg(not(target_arch = "wasm32"))]
    { let _ = (context, binding_id, arguments, argument_count, result); 1 }
}

#[inline]
fn imported_output(context: i32, value: *const Value) {
    #[cfg(target_arch = "wasm32")]
    unsafe { output(context, value) }
    #[cfg(not(target_arch = "wasm32"))]
    { let _ = (context, value); }
}

#[inline]
fn host_unix_milliseconds() -> f64 {
    #[cfg(target_arch = "wasm32")]
    unsafe { unix_milliseconds() }
    #[cfg(not(target_arch = "wasm32"))]
    { 0.0 }
}

#[inline]
fn host_monotonic_milliseconds() -> f64 {
    #[cfg(target_arch = "wasm32")]
    unsafe { monotonic_milliseconds() }
    #[cfg(not(target_arch = "wasm32"))]
    { 0.0 }
}

static mut LAST_OUTPUT_BITS: u64 = 0;
static mut LAST_OUTPUT_TAG: u32 = TAG_NUMBER;
static mut ACTIVE_VM: *mut Vm = std::ptr::null_mut();

thread_local! {
    static VMS: RefCell<Vec<Option<Box<Vm>>>> = RefCell::new(Vec::new());
}

fn with_vm_mut<T>(handle: i32, action: impl FnOnce(&mut Vm) -> Result<T, VmError>) -> Result<T, VmError> {
    if handle <= 0 { return Err(VmError::Bounds); }
    VMS.with(|storage| {
        let mut storage = storage.borrow_mut();
        let vm = storage.get_mut(handle as usize - 1).and_then(Option::as_mut).ok_or(VmError::Bounds)?;
        unsafe { ACTIVE_VM = &mut **vm; }
        let result = action(vm);
        unsafe { ACTIVE_VM = std::ptr::null_mut(); }
        result
    })
}

/// Allocate exactly `length` bytes in Wasm linear memory.
#[no_mangle]
pub extern "C" fn code_alloc(length: usize) -> *mut u8 {
    let mut bytes = Vec::<u8>::with_capacity(length);
    let pointer = bytes.as_mut_ptr();
    mem::forget(bytes);
    pointer
}

/// Release a buffer previously returned by [`code_alloc`].
///
/// # Safety
/// `pointer` and `length` must be the unchanged values used for allocation.
#[no_mangle]
pub unsafe extern "C" fn code_dealloc(pointer: *mut u8, length: usize) {
    if !pointer.is_null() {
        drop(Vec::from_raw_parts(pointer, 0, length));
    }
}

/// Decode and execute one bytecode-v11 artifact. Zero indicates success.
///
/// # Safety
/// The caller must provide a readable linear-memory range of `length` bytes.
#[no_mangle]
pub unsafe extern "C" fn code_run(pointer: *const u8, length: usize) -> i32 {
    if pointer.is_null() { return VmError::InvalidArtifact.status(); }
    let bytes = slice::from_raw_parts(pointer, length);
    let artifact = match parse(bytes) {
        Ok(artifact) => artifact,
        Err(error) => return error.status(),
    };
    let mut vm = Vm::new(artifact);
    ACTIVE_VM = &mut vm;
    match vm.run() {
        Ok(()) => {
            ACTIVE_VM = std::ptr::null_mut();
            LAST_OUTPUT_BITS = vm.output.payload;
            LAST_OUTPUT_TAG = vm.output.tag;
            0
        }
        Err(error) => { ACTIVE_VM = std::ptr::null_mut(); error.status() },
    }
}

/// Create a persistent VM. A positive value is a handle; negative values are
/// negated status codes.
#[no_mangle]
pub unsafe extern "C" fn code_vm_create(pointer: *const u8, length: usize) -> i32 {
    if pointer.is_null() { return -VmError::InvalidArtifact.status(); }
    let artifact = match parse(slice::from_raw_parts(pointer, length)) {
        Ok(artifact) => artifact,
        Err(error) => return -error.status(),
    };
    VMS.with(|storage| {
        let mut storage = storage.borrow_mut();
        let index = storage.iter().position(Option::is_none).unwrap_or(storage.len());
        let mut vm = Box::new(Vm::new(artifact));
        vm.host_context = index as i32 + 1;
        if index == storage.len() { storage.push(Some(vm)); } else { storage[index] = Some(vm); }
        index as i32 + 1
    })
}

#[no_mangle]
pub extern "C" fn code_vm_destroy(handle: i32) {
    if handle <= 0 { return; }
    VMS.with(|storage| {
        if let Some(entry) = storage.borrow_mut().get_mut(handle as usize - 1) { *entry = None; }
    });
}

#[no_mangle]
pub extern "C" fn code_vm_run(handle: i32) -> i32 {
    match with_vm_mut(handle, |vm| {
        let result = vm.run();
        vm.last_error = result.as_ref().err().copied();
        result
    }) { Ok(()) => 0, Err(error) => error.status() }
}

#[no_mangle]
pub extern "C" fn code_vm_create_object(handle: i32, type_id: i32) -> i32 {
    match with_vm_mut(handle, |vm| vm.allocate_object(type_id as usize, false)) {
        Ok(value) => value.payload as i32,
        Err(error) => -error.status(),
    }
}

#[no_mangle]
pub extern "C" fn code_vm_invoke_object(handle: i32, target_byte_ip: i32, frame_size: i32, object_handle: i32) -> i32 {
    match with_vm_mut(handle, |vm| {
        if target_byte_ip < 0 || frame_size < 0 || object_handle < 0 { return Err(VmError::Bounds); }
        let target = *vm.byte_targets.get(target_byte_ip as usize).ok_or(VmError::InvalidTarget)?;
        if target == usize::MAX { return Err(VmError::InvalidTarget); }
        let saved_frame_base = vm.frame_base;
        let saved_frame_size = vm.frame_size;
        let saved_locals_top = vm.locals_top;
        let saved_stack_pointer = vm.stack_pointer;
        let saved_frame_pointer = vm.frame_pointer;
        let new_frame_size = (frame_size as usize).max(1);
        let new_frame_base = vm.locals_top;
        let new_top = new_frame_base.checked_add(new_frame_size).ok_or(VmError::Capacity)?;
        if new_top > vm.locals.len() { return Err(VmError::Capacity); }
        vm.locals[new_frame_base..new_top].fill(Value::default());
        vm.locals[new_frame_base] = Value::handle(TAG_OBJECT, object_handle as usize);
        if vm.profile_enabled { vm.profile_frame_allocations += 1; }
        vm.frame_base = new_frame_base;
        vm.frame_size = new_frame_size;
        vm.locals_top = new_top;
        vm.frame_pointer = 0;
        vm.profile_enter_function(target);
        let result = vm.run_from(target);
        vm.last_error = result.as_ref().err().copied();
        vm.frame_base = saved_frame_base;
        vm.frame_size = saved_frame_size;
        vm.locals_top = saved_locals_top;
        vm.stack_pointer = saved_stack_pointer;
        vm.frame_pointer = saved_frame_pointer;
        result
    }) { Ok(()) => 0, Err(error) => error.status() }
}

#[no_mangle]
pub extern "C" fn code_vm_profile_set_enabled(handle: i32, enabled: i32) -> i32 {
    match with_vm_mut(handle, |vm| { vm.profile_enabled = enabled != 0; Ok(()) }) { Ok(()) => 0, Err(error) => error.status() }
}

#[no_mangle]
pub extern "C" fn code_vm_profile_reset(handle: i32) -> i32 {
    match with_vm_mut(handle, |vm| { vm.reset_profile(); Ok(()) }) { Ok(()) => 0, Err(error) => error.status() }
}

#[no_mangle]
pub extern "C" fn code_vm_profile_instruction_count(handle: i32) -> f64 {
    with_vm_mut(handle, |vm| Ok(vm.profile_instruction_count as f64)).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_opcode_count(handle: i32, opcode: i32) -> f64 {
    with_vm_mut(handle, |vm| Ok(vm.profile_opcodes.get(opcode as usize).copied().unwrap_or(0) as f64)).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_metric(handle: i32, metric: i32) -> f64 {
    with_vm_mut(handle, |vm| Ok(match metric {
        0 => vm.profile_object_allocations as f64,
        1 => vm.profile_array_allocations as f64,
        2 => vm.profile_frame_allocations as f64,
        3 => vm.profile_host_calls as f64,
        4 => vm.profile_stack_high_water as f64,
        5 => vm.profile_locals_high_water as f64,
        6 => vm.profile_call_high_water as f64,
        7 => vm.garbage_collections as f64,
        _ => 0.0,
    })).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_decoded_instruction_count(handle: i32) -> f64 {
    with_vm_mut(handle, |vm| Ok(vm.instructions.len() as f64)).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_function_count(handle: i32) -> i32 {
    with_vm_mut(handle, |vm| Ok(vm.callables.len() as i32)).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_function_name_pointer(handle: i32, index: i32) -> *const u8 {
    let pointer = with_vm_mut(handle, |vm| Ok(vm.callables.get(index as usize).map_or(0, |value| value.name.as_ptr() as usize))).unwrap_or(0);
    pointer as *const u8
}

#[no_mangle]
pub extern "C" fn code_vm_profile_function_name_length(handle: i32, index: i32) -> usize {
    with_vm_mut(handle, |vm| Ok(vm.callables.get(index as usize).map_or(0, |value| value.name.len()))).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_function_metric(handle: i32, index: i32, metric: i32) -> f64 {
    with_vm_mut(handle, |vm| {
        let value = vm.profile_functions.get(index as usize).copied().unwrap_or_default();
        Ok(match metric { 0 => value.calls as f64, 1 => value.inclusive_ms, 2 => value.self_ms, _ => 0.0 })
    }).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_host_count(handle: i32) -> i32 {
    with_vm_mut(handle, |vm| Ok(vm.hosts.len() as i32)).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_host_name_pointer(handle: i32, index: i32) -> *const u8 {
    let pointer = with_vm_mut(handle, |vm| Ok(vm.hosts.get(index as usize).map_or(0, |value| value.symbol.as_ptr() as usize))).unwrap_or(0);
    pointer as *const u8
}

#[no_mangle]
pub extern "C" fn code_vm_profile_host_name_length(handle: i32, index: i32) -> usize {
    with_vm_mut(handle, |vm| Ok(vm.hosts.get(index as usize).map_or(0, |value| value.symbol.len()))).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_profile_host_metric(handle: i32, index: i32, metric: i32) -> f64 {
    with_vm_mut(handle, |vm| Ok(match metric {
        0 => vm.profile_host_calls_by_binding.get(index as usize).copied().unwrap_or(0) as f64,
        1 => vm.profile_host_ms_by_binding.get(index as usize).copied().unwrap_or(0.0),
        _ => 0.0,
    })).unwrap_or(0.0)
}

#[no_mangle]
pub extern "C" fn code_vm_last_error_metric(handle: i32, metric: i32) -> i32 {
    with_vm_mut(handle, |vm| {
        let (line, column, source_id) = vm.debug_location(vm.current_byte_ip);
        Ok(match metric {
            0 => vm.last_error.map(VmError::status).unwrap_or(0),
            1 => vm.current_byte_ip as i32,
            2 => line,
            3 => column,
            4 => source_id,
            _ => -1,
        })
    }).unwrap_or(-1)
}

#[no_mangle]
pub extern "C" fn code_vm_source_pointer(handle: i32, source_id: i32) -> *const u8 {
    with_vm_mut(handle, |vm| {
        if source_id < 0 { return Ok(std::ptr::null()); }
        Ok(vm.source_paths.get(source_id as usize).map_or(std::ptr::null(), Vec::as_ptr))
    }).unwrap_or(std::ptr::null())
}

#[no_mangle]
pub extern "C" fn code_vm_source_length(handle: i32, source_id: i32) -> usize {
    with_vm_mut(handle, |vm| {
        if source_id < 0 { return Ok(0); }
        Ok(vm.source_paths.get(source_id as usize).map_or(0, Vec::len))
    }).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_last_error_frame_count(handle: i32) -> i32 {
    with_vm_mut(handle, |vm| Ok(vm.frame_pointer as i32)).unwrap_or(0)
}

#[no_mangle]
pub extern "C" fn code_vm_last_error_frame_metric(handle: i32, frame_from_top: i32, metric: i32) -> i32 {
    with_vm_mut(handle, |vm| {
        if frame_from_top < 0 || frame_from_top as usize >= vm.frame_pointer {
            return Ok(-1);
        }
        let index = vm.frame_pointer - 1 - frame_from_top as usize;
        let frame = vm.call_frames[index];
        let (line, column, source_id) = vm.debug_location(frame.call_byte_ip);
        Ok(match metric {
            0 => frame.call_byte_ip as i32,
            1 => line,
            2 => column,
            3 => source_id,
            _ => -1,
        })
    }).unwrap_or(-1)
}

#[no_mangle]
pub extern "C" fn code_active_string_pointer(handle: i32) -> *const u8 {
    unsafe {
        ACTIVE_VM.as_ref().and_then(|vm| vm.strings.get(handle as usize)).map_or(std::ptr::null(), Vec::as_ptr)
    }
}

#[no_mangle]
pub extern "C" fn code_active_string_length(handle: i32) -> usize {
    unsafe { ACTIVE_VM.as_ref().and_then(|vm| vm.strings.get(handle as usize)).map_or(0, Vec::len) }
}

#[no_mangle]
pub extern "C" fn code_active_array_length(handle: i32) -> usize {
    unsafe { ACTIVE_VM.as_ref().and_then(|vm| vm.arrays.get(handle as usize)).map_or(0, Vec::len) }
}

#[no_mangle]
pub extern "C" fn code_active_array_number(handle: i32, index: usize) -> f64 {
    unsafe {
        ACTIVE_VM.as_ref().and_then(|vm| vm.arrays.get(handle as usize)).and_then(|array| array.get(index))
            .and_then(|value| value.number_value().ok()).unwrap_or(f64::NAN)
    }
}

#[no_mangle]
pub extern "C" fn code_last_output_number() -> f64 {
    unsafe {
        if LAST_OUTPUT_TAG == TAG_NUMBER { f64::from_bits(LAST_OUTPUT_BITS) } else { f64::NAN }
    }
}

#[no_mangle]
pub extern "C" fn code_value_size() -> usize { mem::size_of::<Value>() }

#[cfg(test)]
mod tests {
    use super::*;

    fn artifact(code: &[u8]) -> Vec<u8> {
        let mut bytes = b"CODE".to_vec();
        bytes.push(BYTECODE_VERSION);
        bytes.extend_from_slice(&(code.len() as i32).to_le_bytes());
        bytes.extend_from_slice(&0i32.to_le_bytes());
        bytes.extend_from_slice(code);
        bytes.extend_from_slice(b"META");
        bytes.extend_from_slice(&24i32.to_le_bytes());
        for _ in 0..6 { bytes.extend_from_slice(&0i32.to_le_bytes()); }
        bytes
    }

    #[test]
    fn value_representation_is_sixteen_bytes() {
        assert_eq!(mem::size_of::<Value>(), 16);
    }

    #[test]
    fn parses_and_executes_minimal_v12_artifact() {
        let parsed = parse(&artifact(&[0x01, 42, 0, 0, 0, 0xFF])).unwrap();
        let mut vm = Vm::new(parsed);
        vm.run().unwrap();
        assert_eq!(vm.stack_pointer, 1);
        assert_eq!(vm.stack[0].number_value().unwrap(), 42.0);
    }

    #[test]
    fn rejects_v9_and_truncated_metadata() {
        let mut old = artifact(&[0xFF]);
        old[4] = 9;
        assert_eq!(parse(&old).err(), Some(VmError::UnsupportedVersion));
        let mut truncated = artifact(&[0xFF]);
        truncated.pop();
        assert!(matches!(parse(&truncated), Err(VmError::InvalidMetadata | VmError::Truncated)));
    }

    #[test]
    fn rejects_branch_into_an_operand() {
        let code = [0x0A, 14, 0, 0, 0, 0xFF];
        assert_eq!(parse(&artifact(&code)).err(), Some(VmError::InvalidTarget));
    }

    #[test]
    fn tracing_gc_preserves_roots_and_reclaims_unreachable_payloads() {
        let parsed = parse(&artifact(&[0xFF])).unwrap();
        let mut vm = Vm::new(parsed);
        let rooted = vm.allocate_string(b"rooted".to_vec());
        let unreachable = vm.allocate_string(b"unreachable".to_vec());
        vm.push(rooted).unwrap();
        vm.allocations_since_gc = 4096;
        vm.collect_garbage();
        assert_eq!(vm.strings[rooted.payload as usize], b"rooted");
        assert!(vm.strings[unreachable.payload as usize].is_empty());
        assert_eq!(vm.garbage_collections, 1);
    }

    #[test]
    fn tracing_gc_does_not_keep_dequeued_queue_values_alive() {
        let parsed = parse(&artifact(&[0xFF])).unwrap();
        let mut vm = Vm::new(parsed);
        let dequeued = vm.allocate_string(b"dequeued".to_vec());
        let queue_handle = vm.queues.len();
        vm.queues.push(vec![dequeued]);
        vm.queue_heads.push(1);
        vm.globals[0] = Value::handle(TAG_QUEUE, queue_handle);
        vm.collect_garbage();
        assert!(
            vm.strings[dequeued.payload as usize].is_empty(),
            "dequeued queue entries should not be traced as live GC roots"
        );
    }

    #[test]
    fn tracing_gc_reuses_unreachable_string_slots() {
        let parsed = parse(&artifact(&[0xFF])).unwrap();
        let mut vm = Vm::new(parsed);
        let baseline = vm.strings.len();
        let allocation_count = 4096;
        for cycle in 0..3 {
            for index in 0..allocation_count {
                vm.allocate_string(format!("cycle-{cycle}-{index}").into_bytes());
            }
            vm.collect_garbage();
            assert!(
                vm.strings.len() <= baseline + allocation_count,
                "unreachable string slots should be reused after GC instead of growing from {baseline} to {}",
                vm.strings.len()
            );
        }
    }

    #[test]
    fn profiling_counters_are_inert_until_enabled() {
        let parsed = parse(&artifact(&[0x01, 1, 0, 0, 0, 0xFF])).unwrap();
        let mut vm = Vm::new(parsed);
        vm.run().unwrap();
        assert_eq!(vm.profile_instruction_count, 0);
        assert_eq!(vm.profile_opcodes[0x01], 0);
        vm.profile_enabled = true;
        vm.reset_profile();
        vm.run().unwrap();
        assert_eq!(vm.profile_instruction_count, 2);
        assert_eq!(vm.profile_opcodes[0x01], 1);
        assert_eq!(vm.profile_opcodes[0xFF], 1);
    }
}
