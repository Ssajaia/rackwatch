# RackWatch

IoT server-rack monitoring: a virtual/real ESP32 publishes sensor data over MQTT, and a desktop app and an Android app show live data, alerts and controls.

**Lecturer:** Zurab Topuria

**Contributors:** Saba Sajaia ([Ssajaia](https://github.com/Ssajaia)), Tiko Gudadze (Tgudadze), Archil Nikoleishvili (Nikol4dev)

## Structure

| Folder       | Description                                                                                    |
| ------------ | ---------------------------------------------------------------------------------------------- |
| `broker/`    | Mosquitto config: port 1883, no anonymous access, users `esp32`, `desktop`, `phone`, ACL rules |
| `simulator/` | Virtual ESP32 (Python), for use until the real board arrives                                   |
| `desktop/`   | .NET MAUI app: login, live data, temperature chart, alerts, controls, SQLite history           |
| `android/`   | Kotlin app: broker login, live data, controls, loud notification on critical alerts            |

Default passwords: `esp32pass`, `desktoppass`, `phonepass`. To change them, edit the broker commands below, `simulator.py` and `Mqtt.cs`.

Data is stored in `Documents\RackWatch`: `users.json` (SHA-256 password hashes) and `history.db` (tables `readings`, `alerts`).
