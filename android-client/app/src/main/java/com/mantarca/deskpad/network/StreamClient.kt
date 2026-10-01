package com.mantarca.deskpad.network

import android.os.Build
import android.util.Log
import com.mantarca.deskpad.decoder.H264Decoder
import com.mantarca.deskpad.protocol.PacketType
import com.mantarca.deskpad.protocol.StreamPacket
import kotlinx.coroutines.*
import org.json.JSONObject
import java.net.InetSocketAddress
import java.net.Socket

class StreamClient(
    private val decoder: H264Decoder,
    private val screenWidth: Int,
    private val screenHeight: Int,
    private val refreshRate: Int,
    private val getTargetIp: () -> String,
    private val getTargetPort: () -> Int,
    private val onStateChanged: (isConnected: Boolean, isStreaming: Boolean, message: String) -> Unit
) {
    private var job: Job? = null
    private var isRunning = false

    fun start() {
        if (isRunning) return
        isRunning = true

        job = CoroutineScope(Dispatchers.IO).launch {
            while (isRunning) {
                var socket: Socket? = null
                try {
                    val ip = getTargetIp()
                    val port = getTargetPort()
                    val connectionType = if (ip == "127.0.0.1") "USB (ADB)" else "Wi-Fi ($ip)"
                    
                    withContext(Dispatchers.Main) {
                        onStateChanged(false, false, "Sunucuya bağlanılıyor ($connectionType)...")
                    }

                    socket = Socket()
                    socket.tcpNoDelay = true
                    socket.connect(InetSocketAddress(ip, port), 2000)

                    val output = socket.getOutputStream()
                    val input = socket.getInputStream()

                    withContext(Dispatchers.Main) {
                        onStateChanged(true, false, "Bağlandı ($connectionType)! El sıkışılıyor...")
                    }

                    // Send Handshake
                    val handshakeJson = JSONObject().apply {
                        put("Width", screenWidth)
                        put("Height", screenHeight)
                        put("RefreshRate", refreshRate)
                        put("DeviceModel", "${Build.MANUFACTURER} ${Build.MODEL}")
                    }.toString()

                    val handshakePacket = StreamPacket.serialize(
                        PacketType.HANDSHAKE,
                        0,
                        handshakeJson.toByteArray(Charsets.UTF_8)
                    )
                    output.write(handshakePacket)
                    output.flush()

                    var streamingNotified = false

                    // Read incoming stream packets
                    while (isRunning && socket.isConnected) {
                        val packet = StreamPacket.readFromStream(input) ?: break

                        when (packet.type) {
                            PacketType.CONFIG_ACK -> {
                                val configStr = String(packet.payload, Charsets.UTF_8)
                                val json = JSONObject(configStr)
                                val w = json.optInt("Width", screenWidth)
                                val h = json.optInt("Height", screenHeight)
                                withContext(Dispatchers.Main) {
                                    decoder.init(w, h)
                                }
                            }
                            PacketType.VIDEO_FRAME -> {
                                if (!streamingNotified) {
                                    streamingNotified = true
                                    withContext(Dispatchers.Main) {
                                        onStateChanged(true, true, "Görüntü aktarılıyor (${screenWidth}x${screenHeight})")
                                    }
                                }
                                val isKeyframe = (packet.flags.toInt() and 0x01) != 0
                                decoder.decodeChunk(packet.payload, isKeyframe)
                            }
                            PacketType.PING -> {
                                val pong = StreamPacket.serialize(PacketType.PONG, 0, ByteArray(0))
                                output.write(pong)
                                output.flush()
                            }
                            else -> {}
                        }
                    }
                } catch (e: Exception) {
                    Log.d("StreamClient", "Bağlantı denemesi: ${e.message}")
                } finally {
                    try {
                        socket?.close()
                    } catch (e: Exception) {}
                    withContext(Dispatchers.Main) {
                        onStateChanged(false, false, "Bağlantı bekleniyor...")
                    }
                }

                delay(2000) // Retry connection every 2 seconds
            }
        }
    }

    fun stop() {
        isRunning = false
        job?.cancel()
        job = null
    }
}
