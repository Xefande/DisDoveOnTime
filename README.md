# DisDoveOnTime
Discord message scheduler running on your own computer, made in Unity.
<br/><img width="256" height="256" alt="DisDoveOnTime" src="https://github.com/user-attachments/assets/41bf357f-2243-415e-b6d0-88220cadd1f9" />
## DiscordScheduler

A simple, locally running tool for scheduling Discord posts via **webhooks**.

### Key features
- Scheduled message posting to Discord channels using webhook URLs.
- Attachments: image or video support (up to **10 MB**, non-Nitro limit).
- Configurable missed policy (Send / Mark Missed / Mark Failed) + sleep detection.
- Separate views for targets, posts, settings, and logs.

### Requirements
- A Discord webhook URL for the target channel.
- .NET Framework 4.7.1 + Unity runtime environment.

### Usage
1) Double click on DisDoveOnTime.exe.
2) Add a target by pasting a webhook URL, then create a post (date/time, text, optional media).
3) Schedule it — the background service will send it, and you can track status in the Log view.

## For devs

### Tech stack
- C# (.NET Framework 4.7.1)
- Unity + UI Toolkit
- C++ (native DLL for additional functionality)

### Note about the native DLL
This project uses a C++ native DLL via interop for certain features. Make sure the DLL is available at runtime (e.g., under `Plugins/` or the appropriate platform-specific folder).

