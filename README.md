# MultiRoblox

A self-owned Roblox account manager for Windows — add accounts, launch any of them into a game,
run several game clients at once, and actually close them when you're done.

Built because the popular closed alternative is archived, unauditable, and holds every cookie you own.
This one is source-visible, stores nothing off your machine, and talks only to official
`*.roblox.com` endpoints.

---

## Quick start

**You do not install anything.** MultiRoblox is a single program you run directly.

1. Get the app:
   - **Easiest:** open [**Releases**](../../releases) and from the newest one download either
     `MultiRoblox.exe` (raw — just double-click) or `MultiRoblox-vX.Y.Z-win-x64.zip` (exe + README).
     Nothing to install — the .NET runtime is bundled inside. (SmartScreen the first time:
     *More info → Run anyway*.)
   - **From source:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
     (`winget install Microsoft.DotNet.SDK.8`), then build your own exe + Desktop shortcut:
     ```
     powershell -ExecutionPolicy Bypass -File publish.ps1
     ```
     Result: one self-contained `src/MultiRoblox.App/bin/Release/net8.0-windows/win-x64/publish/MultiRoblox.exe`
     (~180 MB, .NET bundled in — copy it anywhere). Or just `dotnet run --project src/MultiRoblox.App`
     to run without building an exe.

   **Releases are automated.** GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml))
   builds + tests on every push to `main`, then creates/updates a release named after the
   `<Version>` in [`MultiRoblox.App.csproj`](src/MultiRoblox.App/MultiRoblox.App.csproj) (currently
   `v1.0.0`) with the fresh `MultiRoblox.exe` attached.

   - Push a normal code change → the current version's release gets the new build.
   - Ready to call it a new version? Bump `<Version>` (e.g. `1.1.0`) and push → a new `v1.1.0`
     release appears; the old one stays as history.
2. Launch it. Click **Add** (bottom-left), sign in through the built-in login window, and the account
   appears in the sidebar.
3. Click an account → type a **Place ID** → **Join**.

Windows 10/11 x64 only. WebView2 (used by the login window) is already on Windows 11; on Windows 10 it
usually is too, otherwise get the "Evergreen Runtime" from Microsoft.

Your data lives in `%AppData%\MultiRoblox\` — `accounts.dat` (encrypted with your Windows login via
DPAPI), `settings.json`, `themes\`, `logs\`. Nothing leaves your PC.

---

## Features

- **Accounts sidebar** — every account listed on the left, grouped by a label you set, reorderable by
  drag or the ↑/↓ buttons, with a live search box.
- **Health dot** — green = session good, red = needs a re-login, grey = not checked yet. Updates in
  the background so you know an account works before you click it.
- **Add via login window** — an embedded browser opens Roblox's login; do 2FA / captcha there, and the
  session cookie is captured automatically. Your password is never seen or stored.
- **Add via paste** — paste a `.ROBLOSECURITY` cookie (with or without the warning prefix) instead.
- **Join a game** — enter a Place ID and hit Join; optionally add a Job ID to land in one exact
  server. Your last Place/Job ID is remembered per account.
- **Server browser** — lists public servers with player count, ping and FPS; join a selected server,
  "join smallest", or copy a Job ID.
- **Recent & favorite games** — the selected account's recently-played and favorited games, one click
  to load a game's Place ID into the join box.
- **Player finder** — enter usernames or IDs and see who's online / in-game / in Studio; if a friend
  is in a joinable game, one button launches you into their server.
- **Multi-instance** — launching a second account opens a second Roblox client instead of closing the
  first. Toggle it off in Settings to return to Roblox's normal one-at-a-time behaviour.
- **Clean leave** — the **Leave** button on a running instance kills that client's whole process tree
  immediately, so it doesn't linger in the background or the system tray. **Close all** does the lot.
- **Running instances panel** — every client the app started, which account and place it's on, and its
  state (Launching / Running / Disconnected).
- **Background keep-alive** — periodically refreshes each stored session so cookies stay valid without
  you ever manually re-logging in. Interval configurable (0 = off).
- **Auto-relaunch** — optionally rejoin an instance automatically if it disconnects or gets kicked.
- **Account utilities** — view Robux / premium / birthdate / email-verified status, edit your profile
  description, sign out all other sessions, join or leave a group, block or unblock a user.
- **Open in browser** — opens roblox.com in an isolated window already signed in as that account.
- **Copy cookie** — puts the account's `.ROBLOSECURITY` on the clipboard.
- **Local HTTP API** — optional, off by default, bound to `127.0.0.1` and protected by a key you set.
  Endpoints mirror the old tool (`/GetAccounts`, `/LaunchAccount`, `/GetAuthTicket`, `/GetInstances`,
  `/TerminateAccount`, …) so existing helper scripts work.
- **Theming** — Dark and Light built in, plus any custom `themes\<name>.json` colour file.
- **Tray icon** — closing the window minimises to the tray (toggleable); right-click → Quit to exit.

---

## Cookie lifetime

`.ROBLOSECURITY` has no fixed expiry — it stays valid for months as long as it's used, and Roblox
rotates it to a fresh value periodically. MultiRoblox captures every rotation and (via keep-alive)
pings each account on a timer, so **you open the app any day and just play**. A session only actually
dies if you hit "log out everywhere", change the password, get moderated, or Roblox puts a security
hold on the account — those flip the health dot to red and you re-add that one account. Don't also use
the same account in a separate normal browser, or that browser can rotate the cookie out from under
the stored copy.

---

## Projects

| Project | Role |
|---|---|
| `src/MultiRoblox.Core` | storage, Roblox web client, launcher, singleton holder, instance manager |
| `src/MultiRoblox.App` | WPF UI — builds `MultiRoblox.exe` |
| `src/MultiRoblox.WebApi` | in-process localhost control API |
| `tests/MultiRoblox.Tests` | unit tests (`dotnet test`) |

## Notes

Multi-instancing and automated joins are against the Roblox Terms of Service. This is a personal tool;
use it at your own risk. It performs no captcha solving, password changes, or account creation.
