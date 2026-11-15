# CloveStrap

A Roblox launcher for Windows. CloveStrap opens Roblox with your settings, FPS cap, FFlags and mods already applied,
keeps Roblox up to date, and adds extras on top.

## Download

Grab **CloveStrap.exe** from the [latest release](../../releases/latest) and run it. CloveStrap installs itself and
updates itself from this page's releases.

## Features

- **Presets** for FPS, looks, laptops and low-end PCs, plus "Read your PC" to pick settings for you
- **Accounts**: save Roblox accounts, run several at once, alt mode (an account uses as little of your PC as possible),
  join a place ID or follow a player, pick a server, auto-rejoin
- **Mods**: custom sky, character sounds and cursor
- **FFlag editor** with quick flags (only flags Roblox allows)
- **Extras**: anti-AFK, screen crosshair and overlay, Discord status, quieten Roblox when you switch away,
  a username finder for free Roblox names, themes, and a search box (Ctrl+F)
- **Ask the helper**: an optional AI that runs on your PC and answers questions about CloveStrap

CloveStrap only uses things Roblox allows: settings on Roblox's FFlag allowlist, launcher options, and swapping Roblox's
own default files. It doesn't inject into or modify the game.

## Building

Windows only. Run `./build.sh` from Git Bash; it uses the .NET Framework compiler that comes with Windows and writes
`out/CloveStrap.exe`.

## Releasing a new version

Push a tag like `v2.1.1`. GitHub Actions builds `CloveStrap.exe`, stamps the version from the tag, and publishes the
release. Everyone's CloveStrap picks it up within a few hours (or on its next start).
