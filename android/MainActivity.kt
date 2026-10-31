package com.example.rackwatch

import android.Manifest
import android.content.Context
import android.content.Intent
import android.graphics.Color.TRANSPARENT
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private val Background = Color(0xFFF4F6FA)
private val Ink = Color(0xFF111827)
private val Muted = Color(0xFF6B7280)
private val Blue = Color(0xFF2563EB)
private val Green = Color(0xFF16A34A)
private val Amber = Color(0xFFD97706)
private val Red = Color(0xFFDC2626)
private val Gray = Color(0xFF9CA3AF)
private val Rounded = RoundedCornerShape(16.dp)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val lightBars = SystemBarStyle.light(TRANSPARENT, TRANSPARENT)
        enableEdgeToEdge(lightBars, lightBars)
        if (Build.VERSION.SDK_INT >= 33) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 0)

        setContent {
            MaterialTheme(colorScheme = lightColorScheme(primary = Ink, background = Background, surface = Color.White)) {
                var loggedIn by remember { mutableStateOf(Mqtt.loggedIn) }
                Surface(Modifier.fillMaxSize(), color = Background) {
                    if (loggedIn) Dashboard(onSignOut = { loggedIn = false })
                    else Login(onSignIn = { loggedIn = true })
                }
            }
        }
    }
}

@Composable
fun Login(onSignIn: () -> Unit) {
    val context = LocalContext.current
    val prefs = remember { context.getSharedPreferences("rackwatch", Context.MODE_PRIVATE) }
    var host by remember { mutableStateOf(prefs.getString("host", "192.168.1.10")!!) }
    var user by remember { mutableStateOf(prefs.getString("user", "phone")!!) }
    var password by remember { mutableStateOf("") }
    var error by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Column(
        Modifier.fillMaxSize().safeDrawingPadding().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
    ) {
        Text("RackWatch", color = Ink, fontSize = 30.sp, fontWeight = FontWeight.Bold)
        Text("Server room monitoring", Modifier.padding(bottom = 12.dp), color = Muted)
        OutlinedTextField(host, { host = it }, Modifier.fillMaxWidth(), label = { Text("Broker address (laptop IP)") }, singleLine = true)
        OutlinedTextField(user, { user = it }, Modifier.fillMaxWidth(), label = { Text("Username") }, singleLine = true)
        OutlinedTextField(
            password, { password = it }, Modifier.fillMaxWidth(), label = { Text("Password") }, singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        )
        if (error.isNotEmpty()) Text(error, color = Red, fontSize = 13.sp)
        Button(
            onClick = {
                busy = true
                scope.launch {
                    if (Mqtt.login(host.trim(), user.trim(), password)) {
                        prefs.edit().putString("host", host.trim()).putString("user", user.trim()).apply()
                        context.startForegroundService(Intent(context, RackService::class.java))
                        onSignIn()
                    } else {
                        error = "Can't connect. Check the address, the password and that the phone is on the same Wi-Fi."
                    }
                    busy = false
                }
            },
            modifier = Modifier.fillMaxWidth().height(52.dp),
            enabled = !busy,
            shape = RoundedCornerShape(12.dp),
        ) { Text(if (busy) "Connecting..." else "Sign in") }
    }
}

@Composable
fun Dashboard(onSignOut: () -> Unit) {
    val context = LocalContext.current
    val connected by Mqtt.connected.collectAsState()
    val status by Mqtt.status.collectAsState()
    val alerts by Mqtt.alerts.collectAsState()
    val lastSeen by Mqtt.lastSeen.collectAsState()
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = System.currentTimeMillis()
            delay(1000)
        }
    }
    val online = now - lastSeen < 10_000
    val s = status
    val (stateText, stateHint, stateColor) = when {
        !connected -> Triple("No broker connection", "Check the Wi-Fi and that the broker is running", Gray)
        !online -> Triple("Board offline", "No data from the board for 10 s", Gray)
        s?.state == "critical" -> Triple("Critical", "Live data from the board", Red)
        s?.state == "warning" -> Triple("Warning", "Live data from the board", Amber)
        else -> Triple("Normal", "Live data from the board", Green)
    }
    val temp = s?.temp
    val hum = s?.hum
    val tempColor = when {
        temp == null -> Ink
        temp > 30 -> Red
        temp > 27 -> Amber
        else -> Ink
    }
    val humColor = if (hum != null && (hum < 30 || hum > 60)) Amber else Ink

    Column(
        Modifier.fillMaxSize().safeDrawingPadding().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("RackWatch", color = Ink, fontSize = 24.sp, fontWeight = FontWeight.Bold)
                Text("Server room · room1", color = Muted)
            }
            TextButton(onClick = {
                context.stopService(Intent(context, RackService::class.java))
                Mqtt.logout()
                onSignOut()
            }) { Text("Sign out") }
        }

        Surface(Modifier.fillMaxWidth(), shape = Rounded, color = stateColor) {
            Column(Modifier.padding(20.dp)) {
                Text(stateText, color = Color.White, fontSize = 26.sp, fontWeight = FontWeight.Bold)
                Text(stateHint, color = Color.White)
            }
        }

        Column(Modifier.alpha(if (online) 1f else 0.5f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            TileRow(
                Triple("Temperature", temp?.let { "%.1f °C".format(it) } ?: "—", tempColor),
                Triple("Humidity", hum?.let { "%.0f %%".format(it) } ?: "—", humColor),
            )
            TileRow(
                Triple("Ventilation", if (s?.vent == true) "On" else "Off", if (s?.vent == true) Blue else Ink),
                Triple("Server power", if (s?.power == false) "Off" else "On", if (s?.power == false) Red else Green),
            )
            TileRow(
                Triple("Fire", if (s?.flame == true) "Detected" else "OK", if (s?.flame == true) Red else Green),
                Triple("Water leak", if (s?.leak == true) "Detected" else "OK", if (s?.leak == true) Red else Green),
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button({ Mqtt.send("reset") }, Modifier.weight(1f).height(52.dp), shape = RoundedCornerShape(12.dp)) {
                Text("Restore power")
            }
            OutlinedButton({ Mqtt.send("test_vent") }, Modifier.weight(1f).height(52.dp), shape = RoundedCornerShape(12.dp)) {
                Text("Test ventilation")
            }
        }

        Text("Alerts", Modifier.padding(top = 8.dp), color = Ink, fontSize = 18.sp, fontWeight = FontWeight.Bold)
        if (alerts.isEmpty()) Text("No alerts yet", color = Muted)
        alerts.forEach { AlertRow(it) }
    }
}

@Composable
fun TileRow(vararg tiles: Triple<String, String, Color>) {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        tiles.forEach { (title, value, color) ->
            Card(Modifier.weight(1f), shape = Rounded, colors = CardDefaults.cardColors(containerColor = Color.White)) {
                Column(Modifier.padding(16.dp)) {
                    Text(title, color = Muted, fontSize = 13.sp)
                    Text(value, color = color, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
fun AlertRow(alert: Alert) {
    Card(Modifier.fillMaxWidth(), shape = Rounded, colors = CardDefaults.cardColors(containerColor = Color.White)) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("●", color = if (alert.critical) Red else Amber)
            Text(alert.title, Modifier.weight(1f).padding(start = 10.dp), color = Ink, fontWeight = FontWeight.SemiBold)
            Text(TimeFormat.format(Date(alert.time)), color = Muted, fontSize = 13.sp)
        }
    }
}

private val TimeFormat = SimpleDateFormat("HH:mm:ss", Locale.getDefault())
