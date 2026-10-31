using System.Text.Json;

namespace RackWatch;

// rackwatch/room1/status, e.g. {"temp":24.6,"hum":45,"flame":false,"leak":false,"vent":false,"power":true,"state":"normal"}
public record Status(double? Temp, double? Hum, bool Flame, bool Leak, bool Vent, bool Power, string State);

// rackwatch/room1/alert, e.g. {"type":"fire","level":"critical"}
public record Alert(string Type, string Level)
{
    public DateTime Time { get; init; } = DateTime.Now;
    public bool Critical => Level == "critical";
    public string Title => Type switch
    {
        "fire" => "Fire",
        "leak" => "Water leak",
        "heat" => "High temperature",
        "humidity" => "Humidity out of range",
        "sensor_fault" => "Sensor error",
        "emergency_stop" => "Emergency stop",
        _ => Type
    };
}

// One account in users.json; the password is stored only as a SHA-256 hash
public record User(string Name, string Hash);

public static class Json
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
