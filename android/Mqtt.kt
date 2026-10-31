package com.example.rackwatch

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.hivemq.client.mqtt.MqttGlobalPublishFilter
import com.hivemq.client.mqtt.datatypes.MqttQos
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import com.hivemq.client.mqtt.mqtt3.Mqtt3Client
import com.hivemq.client.mqtt.mqtt3.message.publish.Mqtt3Publish
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit

// Broker connection shared by the screen and the background service.
// The board decides the state; the app only shows it and sends commands.
object Mqtt {
    private const val TOPIC = "rackwatch/room1/"
    private var client: Mqtt3AsyncClient? = null

    val connected = MutableStateFlow(false)
    val status = MutableStateFlow<Status?>(null)
    val alerts = MutableStateFlow(emptyList<Alert>())
    val lastSeen = MutableStateFlow(0L)      // time of the last live status, 0 = board offline
    var onCritical: ((Alert) -> Unit)? = null

    @Volatile var loggedIn = false
        private set

    // Persistent session (cleanSession = false): alerts sent while the phone was briefly offline arrive after reconnecting
    suspend fun login(host: String, user: String, password: String): Boolean = withContext(Dispatchers.IO) {
        val c = Mqtt3Client.builder()
            .identifier("rackwatch-phone")
            .serverHost(host)
            .serverPort(1883)
            .simpleAuth().username(user).password(password.toByteArray()).applySimpleAuth()
            .addConnectedListener { connected.value = true; subscribe() }
            .addDisconnectedListener {
                connected.value = false
                if (loggedIn) it.reconnector.reconnect(true).delay(3, TimeUnit.SECONDS)
            }
            .buildAsync()
        c.publishes(MqttGlobalPublishFilter.ALL) { receive(it) }
        client = c
        loggedIn = runCatching { c.connectWith().cleanSession(false).send().get(10, TimeUnit.SECONDS) }.isSuccess
        if (!loggedIn) {
            c.disconnect()
            client = null
        }
        loggedIn
    }

    fun logout() {
        loggedIn = false
        client?.disconnect()
        client = null
        status.value = null
        alerts.value = emptyList()
        lastSeen.value = 0
    }

    fun send(command: String) {
        client?.run { publishWith().topic(TOPIC + "cmd").payload(command.toByteArray()).qos(MqttQos.AT_LEAST_ONCE).send() }
    }

    private fun subscribe() {
        client?.run { subscribeWith().topicFilter(TOPIC + "#").qos(MqttQos.AT_LEAST_ONCE).send() }
    }

    private fun receive(publish: Mqtt3Publish) {
        val text = String(publish.payloadAsBytes)
        when (publish.topic.toString().removePrefix(TOPIC)) {
            "status" -> {
                status.value = Status.parse(text)
                if (!publish.isRetain) lastSeen.value = System.currentTimeMillis()   // retained = old value
            }
            "alert" -> {
                val alert = Alert.parse(text)
                alerts.value = (listOf(alert) + alerts.value).take(50)
                if (alert.critical) onCritical?.invoke(alert)
            }
            "online" -> if (text == "offline") lastSeen.value = 0
        }
    }
}

// Keeps the connection alive with the screen off and turns critical alerts into loud notifications
class RackService : Service() {
    override fun onCreate() {
        super.onCreate()
        val manager = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(NotificationChannel("service", "Connection", NotificationManager.IMPORTANCE_MIN))
        manager.createNotificationChannel(
            NotificationChannel("alerts", "Critical alerts", NotificationManager.IMPORTANCE_HIGH).apply { enableVibration(true) })

        val ongoing = notification("service", "RackWatch", "Watching room 1").setOngoing(true).build()
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, ongoing, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        else startForeground(1, ongoing)

        Mqtt.onCritical = { alert ->
            manager.notify(alert.time.toInt(), notification("alerts", alert.title, "Critical alert in room 1")
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setCategory(NotificationCompat.CATEGORY_ALARM)
                .setAutoCancel(true)
                .build())
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int) = START_NOT_STICKY

    override fun onDestroy() {
        Mqtt.onCritical = null
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun notification(channel: String, title: String, text: String) =
        NotificationCompat.Builder(this, channel)
            .setSmallIcon(android.R.drawable.ic_dialog_alert)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE))
}
