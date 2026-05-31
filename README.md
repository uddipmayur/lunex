# Lunex

**Lunex** is a modern, high-performance desktop hub and media dashboard designed to centralize your game library, track play statistics, and integrate media playback in a unified, glassmorphic Windows interface. 

Built with premium cybernetic aesthetics, Lunex provides a sleek command center for launching games, monitoring progress, and enjoying music seamlessly.

---

## Key Features

*   **Dynamic Game Library**: Manage your local game collection, customize titles, set individual launch arguments, and view cover and icon art.
*   **Persistent Web Media Hub**: Integrated Chromium-based music view supporting web players (like YouTube and Spotify). Media state is persistent, allowing playback to continue uninterrupted while navigating to other views.
*   **Playtime & Usage Tracker**: Automatically monitors and records session durations, total play minutes, and last played timestamps for all games in the library.
*   **Customizable Shell Profiles**: Customize profile details in a central user hub.
*   **Background Self-Updater**: A silent background updater checks for and applies update binaries on application startup, ensuring the shell is always running the latest version.
*   **Single-Instance Architecture**: Automatically detects duplicate launches, prevents multiple processes, and brings the active window to the front.

---

## Technical Stack & Requirements

Lunex is a native Windows desktop client designed with a lightweight footprint and modern architecture.

*   **Framework**: WPF (Windows Presentation Foundation) & Windows Forms
*   **Target Runtime**: .NET 9.0 (built as a self-contained `win-x64` executable)
*   **Web Engine**: Microsoft Edge WebView2 (Chromium-based rendering)
*   **System Requirements**: Windows 10 / Windows 11 (64-bit) with Microsoft Edge WebView2 runtime installed.

---

## Development & Packaging

Authorized developers can compile and package the application using the built-in publishing workflow:

1.  **Prerequisites**:
    *   .NET 9.0 SDK


## License

This project is licensed under the **MIT License** 

Copyright (c) 2026 **Nexus Realm**. All rights reserved.

---

## Security & Secrets Management

To keep this repository secure before pushing to GitHub, the following measures are active:

1. **Git Exclusions (.gitignore)**: A `.gitignore` file is configured in the root directory to prevent local build artifacts (`bin/`, `obj/`), IDE state/settings (`.vs/`, `.vscode/`, `*.user`), and temporary files from being tracked or pushed.
2. **Local Settings Storage**: All local user configurations, custom game libraries, and profile databases (`lunex_settings.json`, `lunex_profile.json`, `lunex_games.json`) are stored dynamically in the user's `%APPDATA%\Lunex` folder. They are completely decoupled from the project codebase and will not be pushed to GitHub.
3. **API Keys and Public Tokens**:
    * The Supabase credentials located in [UpdateService.cs] use the public anonymous (`anon`) role key. This token is designed to be client-side facing and is safe to commit.
    * **WARNING**: Never replace this with a Supabase `service_role` or admin key, or commit any other private secrets. Always verify code changes for raw credentials before pushing.

