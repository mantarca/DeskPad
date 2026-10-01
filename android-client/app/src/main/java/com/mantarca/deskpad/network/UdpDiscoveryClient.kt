package com.mantarca.deskpad.network

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException

data class DiscoveredPc(
    val pcName: String,
    val ip: String,
    val port: Int
)

class UdpDiscoveryClient {
    private val TAG = "UdpDiscovery"
    private val DISCOVERY_PORT = 2829

    suspend fun discoverPc(): DiscoveredPc? = withContext(Dispatchers.IO) {
        var socket: DatagramSocket? = null
        try {
            socket = DatagramSocket()
            socket.broadcast = true
            socket.soTimeout = 2000 // 2 seconds timeout

            val discoverMsg = """{"type":"TABL_DISCOVER"}"""
            val sendData = discoverMsg.toByteArray(Charsets.UTF_8)

            // Broadcast to 255.255.255.255
            val broadcastAddress = InetAddress.getByName("255.255.255.255")
            val sendPacket = DatagramPacket(sendData, sendData.size, broadcastAddress, DISCOVERY_PORT)
            socket.send(sendPacket)

            Log.d(TAG, "Discovery broadcast sent")

            val receiveData = ByteArray(1024)
            val receivePacket = DatagramPacket(receiveData, receiveData.size)

            socket.receive(receivePacket)
            
            val response = String(receivePacket.data, 0, receivePacket.length, Charsets.UTF_8)
            Log.d(TAG, "Received response: $response")

            val json = JSONObject(response)
            if (json.optString("type") == "TABL_ANNOUNCE") {
                return@withContext DiscoveredPc(
                    pcName = json.optString("pcName", "Unknown PC"),
                    ip = receivePacket.address.hostAddress ?: "127.0.0.1",
                    port = json.optInt("port", 2828)
                )
            }
        } catch (e: SocketTimeoutException) {
            Log.d(TAG, "Discovery timeout")
        } catch (e: Exception) {
            Log.e(TAG, "Discovery error: ${e.message}")
        } finally {
            socket?.close()
        }
        return@withContext null
    }
}
