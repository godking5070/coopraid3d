using System;

namespace RaidRogue
{
    // Only the UI shell is stubbed; the new-run gate is extracted from CampaignUI.cs.
    internal sealed class CampaignRuntime
    {
        public static CampaignRuntime Instance;
        public object Saved, Settlement;
        public string LastSaveLabel = "existing progress";
    }

    internal sealed class CampaignNewRunTests
    {
        private Action _confirm;
        private int _dialogs;
        private void CampaignDialog(string message, Action action) { _dialogs++; _confirm = action; }
        /* ACTUAL_NEW_RUN_METHOD */

        private static int _checks;
        private static void Check(bool value, string message)
        {
            _checks++;
            if (!value) { Console.Error.WriteLine("FAIL: " + message); Environment.Exit(1); }
        }
        private static void Scenario(CampaignRuntime state, bool needsConfirmation, string name)
        {
            CampaignRuntime.Instance = state;
            var ui = new CampaignNewRunTests();
            int started = 0;
            ui.RequestNewCampaign(() => started++);
            Check(started == (needsConfirmation ? 0 : 1), name + ": start must wait for required confirmation");
            Check(ui._dialogs == (needsConfirmation ? 1 : 0), name + ": confirmation presence");
            if (!needsConfirmation) return;
            var saved = state.Saved;
            var settlement = state.Settlement;
            ui._confirm = null; // Dismissing the dialog must not start a run or discard progress.
            Check(started == 0 && state.Saved == saved && state.Settlement == settlement, name + ": cancel preserves progress");
            ui.RequestNewCampaign(() => started++);
            ui._confirm();
            Check(started == 1, name + ": confirmed action starts once");
        }
        private static void Main()
        {
            Scenario(null, false, "runtime absent");
            Scenario(new CampaignRuntime(), false, "no saved progress");
            Scenario(new CampaignRuntime { Saved = new object() }, true, "checkpoint only");
            Scenario(new CampaignRuntime { Settlement = new object() }, true, "settlement only");
            Scenario(new CampaignRuntime { Saved = new object(), Settlement = new object() }, true, "checkpoint and settlement");
            Console.WriteLine("PASS: new-run confirmation checks " + _checks + " (actual gate, managed dialog stub; no Unity UI).");
        }
    }
}
