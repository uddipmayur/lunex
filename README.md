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
    *   Inno Setup 6 (for packaging the installer)
    *   Windows SDK (containing `signtool.exe` for code signing)


## License & Repository Status

This repository is **proprietary and private**. It is not open source. 

*   All rights reserved to the original authors.
*   Unauthorized copying, modification, distribution, or reverse-engineering of any code or assets in this repository is strictly prohibited.
