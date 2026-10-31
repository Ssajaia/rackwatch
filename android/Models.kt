package com.example.rackwatch

import org.json.JSONObject

// rackwatch/room1/status, e.g. {"temp":24.6,"hum":45,"flame":false,"leak":false,"vent":false,"power":true,"state":"normal"}
data class Status(
    val temp: Double?,
    val hum: Double?,
    val flame: Boolean,
    val leak: Boolean,
    val vent: Boolean,
    val power: Boolean,
    val state: String,
) {
    companion object {
        fun parse(json: String) = JSONObject(json).run {
            Status(number("temp"), number("hum"), optBoolean("flame"), optBoolean("leak"),
                optBoolean("vent"), optBoolean("power", true), optString("state", "normal"))
        }
    }
}

// rackwatch/room1/alert, e.g. {"type":"fire","level":"critical"}
data class Alert(val type: String, val level: String, val time: Long = System.currentTimeMillis()) {
    val critical get() = level == "critical"
    val title get() = when (type) {
        "fire" -> "Fire"
        "leak" -> "Water leak"
        "heat" -> "High temperature"
        "humidity" -> "Humidity out of range"
        "sensor_fault" -> "Sensor error"
        "emergency_stop" -> "Emergency stop"
        else -> type
    }

    companion object {
        fun parse(json: String) = JSONObject(json).run { Alert(getString("type"), getString("level")) }
    }
}

// null when the board sends "temp":null (sensor error)
private fun JSONObject.number(key: String): Double? = if (isNull(key)) null else getDouble(key)
