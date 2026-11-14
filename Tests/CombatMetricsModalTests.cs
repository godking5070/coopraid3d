using System;
using UnityEngine;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class Time { public static int frameCount; }
    public class GameObject
    {
        public bool activeSelf, parentActive = true;
        public bool activeInHierarchy => activeSelf && parentActive;
        public object transform = new object();
        public void SetActive(bool value) { activeSelf = value; }
    }
}
namespace RaidRogue
{
    public static class RaidUIOrder { public static void Raise(object transform) { } }
    public class CombatMetricsUI
    {
        /* ACTUAL_MODAL_STATE */
        public bool isActiveAndEnabled = true;
        private GameObject _results = new GameObject();
        /* ACTUAL_MODAL_METHODS */
        private static int checks;
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks++;
        }
        public static void Main()
        {
            Time.frameCount = 10;
            ResetModalState();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A new session must not inherit modal input blocking.");
            var first = new CombatMetricsUI();
            first.OpenResultsOverlay();
            Check(ResultsOpen && BlocksUnderlyingInput, "A visible results overlay must block gameplay.");
            first.CloseResults();
            Check(!ResultsOpen && BlocksUnderlyingInput && !first._results.activeSelf, "Closing must hide the overlay but guard the closing frame.");
            Time.frameCount++;
            Check(!BlocksUnderlyingInput, "Gameplay must unlock on the next frame.");

            first.OpenResultsOverlay();
            first._results.parentActive = false;
            Time.frameCount++;
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A hidden HUD ancestor must not leave an invisible input blocker.");
            first._results.parentActive = true;
            Check(ResultsOpen, "Restoring a visible ancestor must restore modal blocking.");
            first.isActiveAndEnabled = false;
            Check(!ResultsOpen, "A disabled presenter is not an open modal even before its callback runs.");
            first.OnDisable();
            Check(!first._results.activeSelf && BlocksUnderlyingInput, "Disable must close the separate overlay and guard that frame.");
            Time.frameCount++;
            first.OpenResultsOverlay();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A disabled presenter must not reopen the modal.");

            first.isActiveAndEnabled = true;
            first.OpenResultsOverlay();
            var second = new CombatMetricsUI();
            second.OpenResultsOverlay();
            Check(!first._results.activeSelf && second._results.activeSelf && ResultsOpen, "Opening a new owner must close the previous owner's overlay.");
            first.CloseResults();
            Time.frameCount++;
            Check(ResultsOpen && BlocksUnderlyingInput, "Old owner cleanup must not unlock the new owner's modal.");
            second._results = null;
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A removed overlay must not leave input locked.");
            ResetModalState();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "Subsystem reset must clear owner and frame state.");
            Console.WriteLine("PASS: combat results modal lifecycle checks " + checks);
        }
    }
}
