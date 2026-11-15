using System;
using RaidRogue;

internal static class CameraControlTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception(message);
    }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.00001, message);
    private static void Direction(double h, double v, double yaw, bool is2D, double expectedX, double expectedZ)
    {
        CameraControlMath.MovementXZ(h, v, CameraControlMath.MovementYaw(is2D, yaw), out double x, out double z);
        Near(x, expectedX, "movement X"); Near(z, expectedZ, "movement Z");
    }

    private static void Movement()
    {
        Direction(0, 1, 0, false, 0, 1);
        Direction(0, 1, 90, false, 1, 0);
        Direction(0, 1, 180, false, 0, -1);
        Direction(0, 1, 270, false, -1, 0);
        Direction(1, 0, 90, false, 0, -1);
        Direction(-1, 0, 90, false, 0, 1);
        Direction(0, -1, 90, false, -1, 0);
        for (int yaw = -720; yaw <= 720; yaw += 15)
        {
            // 2D directions stay aligned to screen even after arbitrary 3D orbiting.
            Direction(0, 1, yaw, true, 0, 1);
            Direction(1, 0, yaw, true, 1, 0);
            Direction(0, -1, yaw, true, 0, -1);
            Direction(-1, 0, yaw, true, -1, 0);
            CameraControlMath.MovementXZ(1, 1, yaw, out double x, out double z);
            Near(Math.Sqrt(x * x + z * z), 1, "diagonal speed equals cardinal speed");
            Direction(0, 0, yaw, false, 0, 0); // Orbiting with no WASD never moves a caster.
        }
        Direction(0, 0.5, 90, false, 0.5, 0);
        // A held W immediately changes basis on each transition; no key-up is needed.
        Direction(0, 1, 90, false, 1, 0);
        Direction(0, 1, 90, true, 0, 1);
        Direction(0, 1, 90, false, 1, 0);
    }

    private static void FramingAndZoom()
    {
        foreach (double aspect in new[] { 9.0 / 16, 1, 4.0 / 3, 16.0 / 9, 21.0 / 9, 32.0 / 9 })
        {
            double size = CameraControlMath.ArenaOrthoSize(18, aspect, 2);
            Check(size >= 20 && size * aspect >= 20 - 0.00001, "whole arena fits landscape and portrait screens");
            Near(CameraControlMath.ClampFocusAxis(18, 18, size), 0, "default view remains centered");
            Near(CameraControlMath.ClampFocusAxis(18, 18, size * aspect), 0, "default view X remains centered");
            for (int i = -30; i <= 30; i++)
            {
                double focus = CameraControlMath.ClampFocusAxis(i, 18, 5);
                Check(focus - 5 >= -18 && focus + 5 <= 18, "zoomed viewport stays within arena bounds");
            }
        }
        Near(CameraControlMath.FollowBlend(0), 0, "wide framing locks arena center");
        Near(CameraControlMath.FollowBlend(1), 1, "fully zoomed framing follows player");
        Check(CameraControlMath.FollowBlend(0.25) < CameraControlMath.FollowBlend(0.75), "follow blends gradually with zoom");
        Near(CameraControlMath.Zoom(17, 1, 2, 6, 30), 15, "wheel up zooms in");
        Near(CameraControlMath.Zoom(17, -1, 2, 6, 30), 19, "wheel down zooms out");
        Near(CameraControlMath.Zoom(17, 100, 2, 6, 30), 6, "3D nearest limit");
        Near(CameraControlMath.Zoom(17, -100, 2, 6, 30), 30, "3D farthest limit");
        Near(CameraControlMath.Zoom(20, 100, 2, 5, 20), 5, "2D nearest limit");
        Near(CameraControlMath.Zoom(5, -100, 2, 5, 20), 20, "2D returns to arena view");
        Near(CameraControlMath.ClampPitch(-500), 20, "cannot orbit below floor");
        Near(CameraControlMath.ClampPitch(500), 80, "cannot flip over player");
        Near(CameraControlMath.WrapYaw(725), 5, "unlimited positive yaw");
        Near(CameraControlMath.WrapYaw(-725), 355, "unlimited negative yaw");
    }

    private static void CursorDragLifecycle()
    {
        var drag = new CameraDragState();
        drag.Step(true, true, false, false, true);
        Check(!drag.Active, "right press starting over UI is ignored");
        drag.Step(false, true, false, false, false);
        Check(!drag.Active, "holding and leaving UI cannot start a drag");
        drag.Step(true, true, false, true, false);
        Check(!drag.Active, "2D ignores orbit presses");
        drag.Step(true, true, false, false, false);
        Check(drag.Active, "3D field press starts orbit");
        drag.Step(false, true, false, false, true);
        Check(drag.Active, "crossing UI during an existing drag does not stall it");
        drag.Step(false, false, false, false, false);
        Check(!drag.Active, "right release stops orbit");
        for (int reason = 0; reason < 3; reason++)
        {
            drag.Step(true, true, false, false, false);
            Check(drag.Active, "fresh press starts orbit again");
            if (reason == 0) drag.Step(false, true, true, false, false); // focus/menu/text block
            if (reason == 1) drag.Step(false, true, false, true, false); // view transition
            if (reason == 2) drag.Cancel(); // disable/scene change/Escape
            Check(!drag.Active, "cancellation releases drag state");
            drag.Step(false, true, false, false, false);
            Check(!drag.Active, "unblocking while held does not recapture cursor");
            drag.Step(true, true, false, false, false);
            Check(!drag.Active, "synthetic press on focus regain cannot recapture cursor");
            drag.Step(false, false, false, false, false);
        }
        drag.Step(true, true, true, false, false);
        Check(!drag.Active, "menus and unfocused windows cannot start orbit");
    }

    public static int Main()
    {
        Check(CameraControlMath.SpectatorIndex(-1, 0, 1) == -1, "wipe leaves no spectator subject");
        Check(CameraControlMath.SpectatorIndex(-1, 3, -1) == 0, "first death chooses first living member");
        Check(CameraControlMath.SpectatorIndex(2, 3, 1) == 0, "next subject wraps");
        Check(CameraControlMath.SpectatorIndex(0, 3, -1) == 2, "previous subject wraps");
        Check(CameraControlMath.SpectatorIndex(2, 2, 1) == 0, "departed subject resets selection");
        Check(CameraControlMath.SpectatorIndex(0, 1, 1) == 0, "single survivor remains selected");
        Movement(); FramingAndZoom(); CursorDragLifecycle();
        Console.WriteLine("PASS: " + _checks + " camera movement, framing, zoom and drag lifecycle assertions.");
        return 0;
    }
}
