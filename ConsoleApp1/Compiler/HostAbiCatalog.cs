using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ConsoleApp1.Compiler;

[Flags]
enum HostAbiTargets
{
    None = 0,
    Native = 1,
    Web = 2,
    All = Native | Web
}

sealed record HostAbiSymbol(
    string Symbol,
    int Arity,
    string Capability,
    HostAbiTargets Targets);

sealed record HostAbiIntrinsic(
    string Name,
    HostAbiSymbol Symbol,
    TypeSymbol ReturnType,
    string ReturnTypeName,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    IReadOnlyList<string> ParameterTypeNames)
{
    public int Arity => Symbol.Arity;
}

static class HostAbiCatalog
{
    private static readonly IReadOnlyDictionary<string, HostAbiSymbol> Symbols = BuildSymbols();

    private static readonly IReadOnlyDictionary<string, HostAbiIntrinsic> Intrinsics = BuildIntrinsics();

    public static HostAbiSymbol StandardInputOutputPrint => Symbols["standard.input_output.print"];

    public static IEnumerable<HostAbiIntrinsic> IntrinsicSignatures => Intrinsics.Values;

    public static bool TryGetSymbol(string symbol, out HostAbiSymbol hostSymbol)
        => Symbols.TryGetValue(symbol, out hostSymbol!);

    public static bool TryGetIntrinsic(string intrinsicName, out HostAbiIntrinsic intrinsic)
        => Intrinsics.TryGetValue(intrinsicName, out intrinsic!);

    public static IEnumerable<string> IntrinsicNames => Intrinsics.Keys;

    public static bool IsSupported(HostAbiSymbol symbol, CompileTarget target)
    {
        HostAbiTargets needed = target switch
        {
            CompileTarget.VmNative => HostAbiTargets.Native,
            CompileTarget.VmWeb => HostAbiTargets.Web,
            _ => HostAbiTargets.None
        };

        return (symbol.Targets & needed) != 0;
    }

    private static IReadOnlyDictionary<string, HostAbiSymbol> BuildSymbols()
    {
        var map = new Dictionary<string, HostAbiSymbol>(StringComparer.Ordinal)
        {
            ["standard.input_output.print"] = new HostAbiSymbol("standard.input_output.print", 1, "standard.input_output", HostAbiTargets.All),
            ["standard.input_output.read_line"] = new HostAbiSymbol("standard.input_output.read_line", 0, "standard.input_output.read_line", HostAbiTargets.Native),
            ["std.io.print"] = new HostAbiSymbol("std.io.print", 1, "standard.input_output", HostAbiTargets.All),
            ["std.io.read_line"] = new HostAbiSymbol("std.io.read_line", 0, "standard.input_output.read_line", HostAbiTargets.Native),
            ["std.time.unix_ms"] = new HostAbiSymbol("std.time.unix_ms", 0, "std.time", HostAbiTargets.All),
            ["std.time.unix_us"] = new HostAbiSymbol("std.time.unix_us", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ns"] = new HostAbiSymbol("std.time.mono_ns", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks"] = new HostAbiSymbol("std.time.mono_ticks", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks_per_second"] = new HostAbiSymbol("std.time.mono_ticks_per_second", 0, "std.time", HostAbiTargets.All),
            ["std.time.sleep_ms"] = new HostAbiSymbol("std.time.sleep_ms", 1, "std.time.sleep_ms", HostAbiTargets.Native),
            ["std.math.minimum"] = new HostAbiSymbol("std.math.minimum", 2, "std.math", HostAbiTargets.All),
            ["std.math.maximum"] = new HostAbiSymbol("std.math.maximum", 2, "std.math", HostAbiTargets.All),
            ["std.math.absolute"] = new HostAbiSymbol("std.math.absolute", 1, "std.math", HostAbiTargets.All),
            ["std.math.sign"] = new HostAbiSymbol("std.math.sign", 1, "std.math", HostAbiTargets.All),
            ["std.math.lerp"] = new HostAbiSymbol("std.math.lerp", 3, "std.math", HostAbiTargets.All),
            ["std.math.sine"] = new HostAbiSymbol("std.math.sine", 1, "std.math", HostAbiTargets.All),
            ["std.math.cosine"] = new HostAbiSymbol("std.math.cosine", 1, "std.math", HostAbiTargets.All),
            ["std.math.random"] = new HostAbiSymbol("std.math.random", 0, "std.math", HostAbiTargets.All),

            ["engine.window.create"] = new HostAbiSymbol("engine.window.create", 3, "engine.window", HostAbiTargets.All),
            ["engine.window.should_close"] = new HostAbiSymbol("engine.window.should_close", 1, "engine.window", HostAbiTargets.All),
            ["engine.window.present"] = new HostAbiSymbol("engine.window.present", 1, "engine.window", HostAbiTargets.All),

            ["engine.input.key_down"] = new HostAbiSymbol("engine.input.key_down", 2, "engine.input", HostAbiTargets.All),
            ["engine.input.key_down_scene"] = new HostAbiSymbol("engine.input.key_down_scene", 1, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_world_x_scene"] = new HostAbiSymbol("engine.input.pointer_world_x_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_world_y_scene"] = new HostAbiSymbol("engine.input.pointer_world_y_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_screen_x_scene"] = new HostAbiSymbol("engine.input.pointer_screen_x_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_screen_y_scene"] = new HostAbiSymbol("engine.input.pointer_screen_y_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_is_down_scene"] = new HostAbiSymbol("engine.input.pointer_is_down_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_was_pressed_scene"] = new HostAbiSymbol("engine.input.pointer_was_pressed_scene", 0, "engine.input", HostAbiTargets.All),
            ["engine.input.pointer_was_released_scene"] = new HostAbiSymbol("engine.input.pointer_was_released_scene", 0, "engine.input", HostAbiTargets.All),

            ["engine.window.camera_view_left_scene"] = new HostAbiSymbol("engine.window.camera_view_left_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_top_scene"] = new HostAbiSymbol("engine.window.camera_view_top_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_width_scene"] = new HostAbiSymbol("engine.window.camera_view_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_height_scene"] = new HostAbiSymbol("engine.window.camera_view_height_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_right_scene"] = new HostAbiSymbol("engine.window.camera_view_right_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_bottom_scene"] = new HostAbiSymbol("engine.window.camera_view_bottom_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_left_scene"] = new HostAbiSymbol("engine.window.camera_safe_left_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_top_scene"] = new HostAbiSymbol("engine.window.camera_safe_top_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_width_scene"] = new HostAbiSymbol("engine.window.camera_safe_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_height_scene"] = new HostAbiSymbol("engine.window.camera_safe_height_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_right_scene"] = new HostAbiSymbol("engine.window.camera_safe_right_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_bottom_scene"] = new HostAbiSymbol("engine.window.camera_safe_bottom_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.screen_width_scene"] = new HostAbiSymbol("engine.window.screen_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.screen_height_scene"] = new HostAbiSymbol("engine.window.screen_height_scene", 0, "engine.window", HostAbiTargets.All),

            ["engine.gfx.clear"] = new HostAbiSymbol("engine.gfx.clear", 5, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.clear_scene"] = new HostAbiSymbol("engine.gfx.clear_scene", 4, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rect"] = new HostAbiSymbol("engine.gfx.draw_rect", 9, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rect_scene"] = new HostAbiSymbol("engine.gfx.draw_rect_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rectangle_scene"] = new HostAbiSymbol("engine.gfx.draw_rectangle_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rectangle_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_rectangle_outline_scene", 9, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_circle_scene"] = new HostAbiSymbol("engine.gfx.draw_circle_scene", 7, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_circle_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_circle_outline_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_polygon_scene"] = new HostAbiSymbol("engine.gfx.draw_polygon_scene", 5, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_polygon_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_polygon_outline_scene", 6, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_line_scene"] = new HostAbiSymbol("engine.gfx.draw_line_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_text_scene"] = new HostAbiSymbol("engine.gfx.draw_text_scene", 10, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_image_scene"] = new HostAbiSymbol("engine.gfx.draw_image_scene", 6, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_sprite_scene"] = new HostAbiSymbol("engine.gfx.draw_sprite_scene", 10, "engine.gfx", HostAbiTargets.All),

            ["engine.diagnostics.last_frame_interval_milliseconds_scene"] = new HostAbiSymbol("engine.diagnostics.last_frame_interval_milliseconds_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.estimated_frames_per_second_scene"] = new HostAbiSymbol("engine.diagnostics.estimated_frames_per_second_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.last_frame_work_milliseconds_scene"] = new HostAbiSymbol("engine.diagnostics.last_frame_work_milliseconds_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.last_update_work_milliseconds_scene"] = new HostAbiSymbol("engine.diagnostics.last_update_work_milliseconds_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.last_draw_work_milliseconds_scene"] = new HostAbiSymbol("engine.diagnostics.last_draw_work_milliseconds_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.last_draw_hud_work_milliseconds_scene"] = new HostAbiSymbol("engine.diagnostics.last_draw_hud_work_milliseconds_scene", 0, "engine.diagnostics", HostAbiTargets.All),
            ["engine.diagnostics.last_update_steps_scene"] = new HostAbiSymbol("engine.diagnostics.last_update_steps_scene", 0, "engine.diagnostics", HostAbiTargets.All),

            ["engine.audio.can_play_sound_scene"] = new HostAbiSymbol("engine.audio.can_play_sound_scene", 0, "engine.audio", HostAbiTargets.All),
            ["engine.audio.play_sound_scene"] = new HostAbiSymbol("engine.audio.play_sound_scene", 2, "engine.audio", HostAbiTargets.All),
            ["engine.audio.play_looping_sound_scene"] = new HostAbiSymbol("engine.audio.play_looping_sound_scene", 2, "engine.audio", HostAbiTargets.All),
            ["engine.audio.stop_sound_scene"] = new HostAbiSymbol("engine.audio.stop_sound_scene", 1, "engine.audio", HostAbiTargets.All),
            ["engine.audio.set_sound_volume_scene"] = new HostAbiSymbol("engine.audio.set_sound_volume_scene", 2, "engine.audio", HostAbiTargets.All),
            ["engine.audio.sound_is_playing_scene"] = new HostAbiSymbol("engine.audio.sound_is_playing_scene", 1, "engine.audio", HostAbiTargets.All),
            ["engine.audio.stop_all_sounds_scene"] = new HostAbiSymbol("engine.audio.stop_all_sounds_scene", 0, "engine.audio", HostAbiTargets.All)
        };

        return new ReadOnlyDictionary<string, HostAbiSymbol>(map);
    }

    private static IReadOnlyDictionary<string, HostAbiIntrinsic> BuildIntrinsics()
    {
        static HostAbiIntrinsic Sig(
            string name,
            string symbol,
            TypeSymbol returnType,
            string returnTypeName,
            params (TypeSymbol Symbol, string Name)[] parameters)
        {
            var paramTypes = new List<TypeSymbol>(parameters.Length);
            var paramTypeNames = new List<string>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                paramTypes.Add(parameters[i].Symbol);
                paramTypeNames.Add(parameters[i].Name);
            }

            return new HostAbiIntrinsic(
                name,
                Symbols[symbol],
                returnType,
                returnTypeName,
                paramTypes,
                paramTypeNames);
        }

        var map = new Dictionary<string, HostAbiIntrinsic>(StringComparer.Ordinal)
        {
            ["unixMilliseconds"] = Sig("unixMilliseconds", "std.time.unix_ms", TypeSymbol.Integer, "integer"),
            ["unixMicroseconds"] = Sig("unixMicroseconds", "std.time.unix_us", TypeSymbol.Integer, "integer"),
            ["monotonicNanoseconds"] = Sig("monotonicNanoseconds", "std.time.mono_ns", TypeSymbol.Integer, "integer"),
            ["monotonicTicks"] = Sig("monotonicTicks", "std.time.mono_ticks", TypeSymbol.Integer, "integer"),
            ["monotonicTicksPerSecond"] = Sig("monotonicTicksPerSecond", "std.time.mono_ticks_per_second", TypeSymbol.Integer, "integer"),
            ["sleepMilliseconds"] = Sig("sleepMilliseconds", "std.time.sleep_ms", TypeSymbol.Void, "void", (TypeSymbol.Integer, "integer")),
            ["readLine"] = Sig("readLine", "standard.input_output.read_line", TypeSymbol.String, "string"),
            ["minimum"] = Sig("minimum", "std.math.minimum", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["maximum"] = Sig("maximum", "std.math.maximum", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["absolute"] = Sig("absolute", "std.math.absolute", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["sign"] = Sig("sign", "std.math.sign", TypeSymbol.Integer, "integer", (TypeSymbol.Real, "real")),
            ["lerp"] = Sig("lerp", "std.math.lerp", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["sine"] = Sig("sine", "std.math.sine", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["cosine"] = Sig("cosine", "std.math.cosine", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["random"] = Sig("random", "std.math.random", TypeSymbol.Real, "real"),

            ["windowCreate"] = Sig(
                "windowCreate",
                "engine.window.create",
                TypeSymbol.Whole,
                "whole",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Integer, "integer"),
                (TypeSymbol.Integer, "integer")),
            ["windowShouldClose"] = Sig(
                "windowShouldClose",
                "engine.window.should_close",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Whole, "whole")),
            ["windowPresent"] = Sig(
                "windowPresent",
                "engine.window.present",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole")),
            ["windowInputKeyDown"] = Sig(
                "windowInputKeyDown",
                "engine.input.key_down",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Integer, "integer")),
            ["inputKeyDown"] = Sig(
                "inputKeyDown",
                "engine.input.key_down_scene",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Integer, "integer")),
            ["inputPointerWorldX"] = Sig("inputPointerWorldX", "engine.input.pointer_world_x_scene", TypeSymbol.Real, "real"),
            ["inputPointerWorldY"] = Sig("inputPointerWorldY", "engine.input.pointer_world_y_scene", TypeSymbol.Real, "real"),
            ["inputPointerScreenX"] = Sig("inputPointerScreenX", "engine.input.pointer_screen_x_scene", TypeSymbol.Real, "real"),
            ["inputPointerScreenY"] = Sig("inputPointerScreenY", "engine.input.pointer_screen_y_scene", TypeSymbol.Real, "real"),
            ["inputPointerIsDown"] = Sig("inputPointerIsDown", "engine.input.pointer_is_down_scene", TypeSymbol.Boolean, "boolean"),
            ["inputPointerWasPressed"] = Sig("inputPointerWasPressed", "engine.input.pointer_was_pressed_scene", TypeSymbol.Boolean, "boolean"),
            ["inputPointerWasReleased"] = Sig("inputPointerWasReleased", "engine.input.pointer_was_released_scene", TypeSymbol.Boolean, "boolean"),
            ["cameraViewLeft"] = Sig("cameraViewLeft", "engine.window.camera_view_left_scene", TypeSymbol.Real, "real"),
            ["cameraViewTop"] = Sig("cameraViewTop", "engine.window.camera_view_top_scene", TypeSymbol.Real, "real"),
            ["cameraViewWidth"] = Sig("cameraViewWidth", "engine.window.camera_view_width_scene", TypeSymbol.Real, "real"),
            ["cameraViewHeight"] = Sig("cameraViewHeight", "engine.window.camera_view_height_scene", TypeSymbol.Real, "real"),
            ["cameraViewRight"] = Sig("cameraViewRight", "engine.window.camera_view_right_scene", TypeSymbol.Real, "real"),
            ["cameraViewBottom"] = Sig("cameraViewBottom", "engine.window.camera_view_bottom_scene", TypeSymbol.Real, "real"),
            ["cameraSafeLeft"] = Sig("cameraSafeLeft", "engine.window.camera_safe_left_scene", TypeSymbol.Real, "real"),
            ["cameraSafeTop"] = Sig("cameraSafeTop", "engine.window.camera_safe_top_scene", TypeSymbol.Real, "real"),
            ["cameraSafeWidth"] = Sig("cameraSafeWidth", "engine.window.camera_safe_width_scene", TypeSymbol.Real, "real"),
            ["cameraSafeHeight"] = Sig("cameraSafeHeight", "engine.window.camera_safe_height_scene", TypeSymbol.Real, "real"),
            ["cameraSafeRight"] = Sig("cameraSafeRight", "engine.window.camera_safe_right_scene", TypeSymbol.Real, "real"),
            ["cameraSafeBottom"] = Sig("cameraSafeBottom", "engine.window.camera_safe_bottom_scene", TypeSymbol.Real, "real"),
            ["screenWidth"] = Sig("screenWidth", "engine.window.screen_width_scene", TypeSymbol.Real, "real"),
            ["screenHeight"] = Sig("screenHeight", "engine.window.screen_height_scene", TypeSymbol.Real, "real"),
            ["gfxClear"] = Sig(
                "gfxClear",
                "engine.gfx.clear",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["clear"] = Sig(
                "clear",
                "engine.gfx.clear_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["gfxDrawRectangle"] = Sig(
                "gfxDrawRectangle",
                "engine.gfx.draw_rect",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawRectangle"] = Sig(
                "drawRectangle",
                "engine.gfx.draw_rectangle_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawRectangleOutline"] = Sig(
                "drawRectangleOutline",
                "engine.gfx.draw_rectangle_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawCircle"] = Sig(
                "drawCircle",
                "engine.gfx.draw_circle_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawCircleOutline"] = Sig(
                "drawCircleOutline",
                "engine.gfx.draw_circle_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawPolygon"] = Sig(
                "drawPolygon",
                "engine.gfx.draw_polygon_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Array, "array<real>"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawPolygonOutline"] = Sig(
                "drawPolygonOutline",
                "engine.gfx.draw_polygon_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Array, "array<real>"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawLine"] = Sig(
                "drawLine",
                "engine.gfx.draw_line_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawText"] = Sig(
                "drawText",
                "engine.gfx.draw_text_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.String, "string"),
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawImage"] = Sig(
                "drawImage",
                "engine.gfx.draw_image_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["drawSprite"] = Sig(
                "drawSprite",
                "engine.gfx.draw_sprite_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["diagnosticsLastFrameIntervalMilliseconds"] = Sig(
                "diagnosticsLastFrameIntervalMilliseconds",
                "engine.diagnostics.last_frame_interval_milliseconds_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsEstimatedFramesPerSecond"] = Sig(
                "diagnosticsEstimatedFramesPerSecond",
                "engine.diagnostics.estimated_frames_per_second_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsLastFrameWorkMilliseconds"] = Sig(
                "diagnosticsLastFrameWorkMilliseconds",
                "engine.diagnostics.last_frame_work_milliseconds_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsLastUpdateWorkMilliseconds"] = Sig(
                "diagnosticsLastUpdateWorkMilliseconds",
                "engine.diagnostics.last_update_work_milliseconds_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsLastDrawWorkMilliseconds"] = Sig(
                "diagnosticsLastDrawWorkMilliseconds",
                "engine.diagnostics.last_draw_work_milliseconds_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsLastDrawHudWorkMilliseconds"] = Sig(
                "diagnosticsLastDrawHudWorkMilliseconds",
                "engine.diagnostics.last_draw_hud_work_milliseconds_scene",
                TypeSymbol.Real,
                "real"),
            ["diagnosticsLastUpdateSteps"] = Sig(
                "diagnosticsLastUpdateSteps",
                "engine.diagnostics.last_update_steps_scene",
                TypeSymbol.Integer,
                "integer"),
            ["audioCanPlaySound"] = Sig(
                "audioCanPlaySound",
                "engine.audio.can_play_sound_scene",
                TypeSymbol.Boolean,
                "boolean"),
            ["audioPlaySound"] = Sig(
                "audioPlaySound",
                "engine.audio.play_sound_scene",
                TypeSymbol.Integer,
                "integer",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real")),
            ["audioPlayLoopingSound"] = Sig(
                "audioPlayLoopingSound",
                "engine.audio.play_looping_sound_scene",
                TypeSymbol.Integer,
                "integer",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real")),
            ["audioStopSound"] = Sig(
                "audioStopSound",
                "engine.audio.stop_sound_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Integer, "integer")),
            ["audioSetSoundVolume"] = Sig(
                "audioSetSoundVolume",
                "engine.audio.set_sound_volume_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Integer, "integer"),
                (TypeSymbol.Real, "real")),
            ["audioSoundIsPlaying"] = Sig(
                "audioSoundIsPlaying",
                "engine.audio.sound_is_playing_scene",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Integer, "integer")),
            ["audioStopAllSounds"] = Sig(
                "audioStopAllSounds",
                "engine.audio.stop_all_sounds_scene",
                TypeSymbol.Void,
                "void")
        };

        return new ReadOnlyDictionary<string, HostAbiIntrinsic>(map);
    }
}
