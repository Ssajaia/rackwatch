"""RackWatch virtual ESP32: same MQTT topics, JSON and safety rules as the real board."""
import json
import time
import tkinter as tk
from tkinter import ttk

import paho.mqtt.client as mqtt

HOST, PORT = "localhost", 1883
USER, PASSWORD = "esp32", "esp32pass"
TOPIC = "rackwatch/room1/"
LEVELS = ("none", "warning", "critical")


class Board:
    """Safety rules from the project document (section 5), without any UI."""

    def __init__(self):
        self.cut = False          # power relay off; stays off until reset
        self.vent = False
        self.vent_test = 0.0      # time when the 3 s ventilation test ends
        self.reason = ""
        self.flame = self.leak = False
        self.levels = {}          # last alert level per type
        self.pending = []

    def hold(self):
        self.cut, self.reason = True, "EMERGENCY STOP"
        self.pending.append({"type": "emergency_stop", "level": "critical"})

    def reset(self):
        if not (self.flame or self.leak):
            self.cut = False

    def update(self, temp, hum, flame, leak, fault):
        """One 2 s cycle. Returns the status and the alerts that became active or more severe."""
        self.flame, self.leak = flame, leak
        if flame or leak:
            self.cut, self.reason = True, "FIRE!" if flame else "LEAK!"
        if fault:
            temp = hum = None
        elif temp > 27:
            self.vent = True
        elif temp < 26:
            self.vent = False

        hot = temp is not None and temp > 27
        overheat = temp is not None and temp > 30
        bad_hum = hum is not None and not 30 <= hum <= 60
        danger = self.cut or overheat
        warning = not danger and (hot or bad_hum or fault)

        levels = {"fire": 2 if flame else 0, "leak": 2 if leak else 0,
                  "heat": 2 if overheat else 1 if hot else 0,
                  "humidity": 1 if bad_hum else 0, "sensor_fault": 1 if fault else 0}
        alerts = self.pending + [{"type": k, "level": LEVELS[v]}
                                 for k, v in levels.items() if v > self.levels.get(k, 0)]
        self.levels, self.pending = levels, []

        status = {"temp": temp, "hum": hum, "flame": flame, "leak": leak,
                  "vent": self.vent or time.time() < self.vent_test, "power": not self.cut,
                  "state": "critical" if danger else "warning" if warning else "normal"}
        return status, alerts


class Simulator:
    def __init__(self, root):
        self.root, self.board = root, Board()
        self.pressed, self.job, self.link = 0.0, "", "Connecting to broker..."
        self.temp, self.hum = tk.DoubleVar(value=24.0), tk.IntVar(value=45)
        self.flame, self.leak, self.fault, self.paused = (tk.BooleanVar() for _ in range(4))

        root.title("RackWatch board simulator")
        root.resizable(False, False)
        inputs = ttk.Frame(root, padding=16)
        inputs.grid(row=0, column=0, sticky="n")
        for row, (text, var, low, high, step) in enumerate(
                (("Temperature, °C", self.temp, 15, 40, 0.5), ("Humidity, %", self.hum, 10, 90, 1))):
            ttk.Label(inputs, text=text).grid(row=row, column=0, sticky="sw", pady=(0, 6))
            tk.Scale(inputs, variable=var, from_=low, to=high, resolution=step,
                     orient="horizontal", length=220).grid(row=row, column=1)
        for i, (text, var) in enumerate((("Flame", self.flame), ("Water leak", self.leak),
                                         ("DHT error", self.fault), ("Pause (board offline)", self.paused))):
            ttk.Checkbutton(inputs, text=text, variable=var).grid(row=2 + i // 2, column=i % 2, sticky="w", pady=4)
        button = ttk.Button(inputs, text="Emergency button  (hold 2 s = stop, click = restore)")
        button.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        button.bind("<ButtonPress-1>", self.press)
        button.bind("<ButtonRelease-1>", self.release)

        board = ttk.Frame(root, padding=16)
        board.grid(row=0, column=1, sticky="n")
        self.screen = tk.Label(board, width=18, height=4, bg="black", fg="white", font=("Consolas", 12),
                               justify="left", anchor="w", padx=8)
        self.screen.pack()
        self.leds = tk.Canvas(board, width=96, height=30, highlightthickness=0)
        self.leds.pack(pady=8)
        self.led_ids = [self.leds.create_oval(9 + i * 30, 6, 27 + i * 30, 24, outline="") for i in range(3)]
        self.relays = ttk.Label(board)
        self.relays.pack()
        self.info = ttk.Label(root, padding=(16, 0, 16, 12), foreground="gray")
        self.info.grid(row=1, column=0, columnspan=2, sticky="w")

        self.client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, "rackwatch-sim")
        self.client.username_pw_set(USER, PASSWORD)
        self.client.will_set(TOPIC + "online", "offline", qos=1, retain=True)
        self.client.on_connect, self.client.on_disconnect = self.on_connect, self.on_disconnect
        self.client.on_message = self.on_message
        self.client.connect_async(HOST, PORT)
        self.client.loop_start()
        self.tick()

    def on_connect(self, client, userdata, flags, reason, properties):
        if reason.is_failure:
            self.link = f"Broker refused: {reason}"
            return
        self.link = f"Connected to {HOST}:{PORT}"
        client.publish(TOPIC + "online", "online", qos=1, retain=True)
        client.subscribe(TOPIC + "cmd", qos=1)

    def on_disconnect(self, client, userdata, flags, reason, properties):
        self.link = "Disconnected, retrying..."

    def on_message(self, client, userdata, msg):
        command = msg.payload.decode()
        if command == "reset":
            self.board.reset()
        elif command == "test_vent":
            self.board.vent_test = time.time() + 3

    def press(self, _):
        self.pressed = time.time()
        self.job = self.root.after(2000, self.board.hold)

    def release(self, _):
        if self.job:
            self.root.after_cancel(self.job)
        if time.time() - self.pressed < 2:
            self.board.reset()

    def tick(self):
        self.root.after(2000, self.tick)
        status, alerts = self.board.update(round(self.temp.get(), 1), self.hum.get(),
                                           self.flame.get(), self.leak.get(), self.fault.get())
        if not self.paused.get():
            self.client.publish(TOPIC + "status", json.dumps(status), retain=True)
            for alert in alerts:
                self.client.publish(TOPIC + "alert", json.dumps(alert), qos=1)
        self.show(status)

    def show(self, s):
        """Mirror of the board's OLED, LEDs and relays."""
        temp = "--.-" if s["temp"] is None else f"{s['temp']:.1f}"
        hum = "--" if s["hum"] is None else s["hum"]
        message = self.board.reason if self.board.cut else "SENSOR ERROR" if s["temp"] is None else s["state"].upper()
        self.screen.config(text=f"RackWatch\n{temp} C   {hum} %\n{message}")
        lit = ("normal", "warning", "critical").index(s["state"])
        for i, (led, color) in enumerate(zip(self.led_ids, ("#22c55e", "#facc15", "#ef4444"))):
            self.leds.itemconfig(led, fill=color if i == lit else "#d1d5db")
        self.relays.config(text=f"Vent relay: {'ON' if s['vent'] else 'OFF'}      "
                                f"Power relay: {'ON' if s['power'] else 'OFF'}")
        self.info.config(text=self.link + ("   (paused)" if self.paused.get() else ""))


if __name__ == "__main__":
    window = tk.Tk()
    Simulator(window)
    window.mainloop()
