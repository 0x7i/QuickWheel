# Radial Command Wheel

A clean and native Windows 11 radial launcher. Press a global shortcut to open a smooth radial menu at your cursor to launch apps, files, folders, commands, or nested groups.

![Main Interface](https://github.com/user-attachments/assets/b7be9614-8c5c-409e-a559-45d736d99d33)

---

## Features

- **Quick Launch:** Access your favorite shortcuts instantly with a customizable global hotkey (Default: `Ctrl + Alt + Space`).

- **Group Management:** Organize up to 12 items per wheel, including nested groups.
  <br>![Group Management UI](https://github.com/user-attachments/assets/3c5f8e4c-ee68-4fa6-ad25-8ba6030fee32)
  <br>![Nested Groups](https://github.com/user-attachments/assets/c9a5947f-fee9-442c-aaf6-0b6e7039c262)

- **Auto Personalization:** Automatic display names and accent color generation based on target icons.
  <br>![Auto Personalization](https://github.com/user-attachments/assets/bb244ece-6d16-4ffc-8680-8d495de772fc)

- **Bilingual Support:** Full English and Arabic UI support with RTL layout.
  <br>![Arabic Support](https://github.com/user-attachments/assets/1d22cd1a-4cb4-4025-917e-405a88442be9)

- **Custom Themes:** Built-in themes with full support for custom colors, opacity, and sizes.

- **Portable Config:** Easily export and import your setup between machines.

---

## Quick Start

1. Download and run `RadialCommandWheel.exe` (Self-contained, no .NET installation required).
2. Press and hold **Ctrl + Alt + Space** to open the wheel.
3. Move your cursor over an item and click to launch.
4. Release the hotkey, press **Esc**, or click outside to cancel.

![Quick Start Flow](https://github.com/user-attachments/assets/c51af57a-2bbf-4eba-93d3-10f98d13ce31)

> **Tip:** You can launch the app with `--background` to keep it running silently in the system tray.

---

## Configuration & Storage

Your personal settings and backups are saved locally at:
`%LOCALAPPDATA%\RadialCommandWheel\`

---

## Troubleshooting

- **Shortcut isn't working:** Check if another app is using the hotkey and change it in settings.
- **Tray icon hidden:** Check Windows taskbar overflow settings (*Settings > Personalization > Taskbar*).
- **Paths broken after import:** Edit the path in management settings to match your new PC layout.
