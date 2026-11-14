$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Unit fixtures use temporary inboxes; no persistent watcher or Codex task is started.
    # unittest's default success output uses stderr, which Windows PowerShell can
    # turn into terminating NativeCommandError records when a caller redirects it.
    & python -B -c "import sys, unittest; suite=unittest.defaultTestLoader.discover('.', pattern='test_bug_report_watch.py'); result=unittest.TextTestRunner(stream=sys.stdout, verbosity=2).run(suite); sys.exit(0 if result.wasSuccessful() else 1)"
    if ($LASTEXITCODE -ne 0) { throw 'Bug report watcher fixture checks failed.' }
    Write-Output 'PASS: bug report queue fixtures; no real inbox, campaign saves or agent tasks used.'
} finally { Pop-Location }
