# Build Troubleshooting — Local Unity Package Manager failure

## Symptom

Every local batchmode run fails identically:

```
[Package Manager] Could not connect to IPC stream "Upm-<pid>" after 30.0 seconds.
[Package Manager] Failed to start the Unity Package Manager local server process.
Application will terminate with return code 1
```

No `Library/` folder is produced, so no compile and no build.

## What was ruled out (with evidence)

| Hypothesis | Verdict | Evidence |
|---|---|---|
| Unity not licensed | **Ruled out** | Log now reports `Product: Unity Personal`, `Type: ULF`, `Expiration: Unlimited` |
| Antivirus blocking `UnityPackageManager.exe` | **Ruled out** | Controlled Folder Access `0`, ASR rule count `0`, Authenticode signature `Valid` (2.2s), and UPM launched successfully once — `upm.log` shows `IPC server started (IPC path=Unity-Upm-6548)` |
| Cold-scan timeout on the 95 MB UPM binary | **Ruled out** | Retry after the binary was cached failed identically |
| Stale Unity / UPM processes holding the pipe | **Ruled out** | Processes killed before a clean retry; same failure |
| Unity Hub services not running | **Ruled out** | Hub started and given 25s to initialize; same failure |

## Key diagnostic detail

`upm.log` contains entries **only from the first run**. On every subsequent
attempt Unity never spawns `UnityPackageManager.exe` at all — it simply waits
30 seconds for a named pipe that nothing ever connects to, then exits.

This points at a corrupt/incomplete Package Manager component in the editor
install rather than an environmental block.

## Remaining local fixes to try (in order of cost)

1. **Reboot.** Orphaned named-pipe registrations are a classic cause and survive
   process kills but not a restart. Cheapest option; try first.
2. **Clear the Unity caches:** delete `%LOCALAPPDATA%\Unity\cache` and
   `%LOCALAPPDATA%\Unity\Editor\upm.log`, then retry.
3. **Repair the editor install** via Unity Hub → Installs → the `6000.5.6f1`
   gear icon → uninstall and reinstall, ensuring the **WebGL Build Support**
   module is re-selected.

## Chosen workaround: build in CI

`.github/workflows/build-webgl.yml` builds the WebGL player in a clean Linux
container via GameCI and publishes it to GitHub Pages. This removes the
dependency on the local install entirely and is reproducible.

### Required repository secrets

Add these under **Settings → Secrets and variables → Actions**:

| Secret | Value |
|---|---|
| `UNITY_EMAIL` | The email for the Unity account holding the Personal license |
| `UNITY_PASSWORD` | That account's password |
| `UNITY_LICENSE` | *(optional)* full contents of the `.ulf` license file, if activation by email/password alone fails |

Then enable **Settings → Pages → Source: GitHub Actions**.

> Credentials must be entered by the repository owner directly in the GitHub UI.
> They should never be pasted into a chat, committed to the repo, or shared with
> an agent.
