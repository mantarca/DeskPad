package com.mantarca.deskpad.protocol

import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class PacketType(val code: Byte) {
    HANDSHAKE(0x01),
    CONFIG_ACK(0x02),
    VIDEO_FRAME(0x10),
    PING(0x20),
    PONG(0x21);

    companion object {
        fun fromByte(b: Byte) = entries.firstOrNull { it.code == b } ?: VIDEO_FRAME
    }
}

class StreamPacket(
    val type: PacketType,
    val flags: Byte,
    val payload: ByteArray
) {
    companion object {
        val MAGIC = byteArrayOf(0x54, 0x41, 0x42, 0x4C) // "TABL"
        const val HEADER_SIZE = 12

        fun serialize(type: PacketType, flags: Byte, payload: ByteArray): ByteArray {
            val buffer = ByteBuffer.allocate(HEADER_SIZE + payload.size)
            buffer.order(ByteOrder.BIG_ENDIAN)
            buffer.put(MAGIC)
            buffer.put(type.code)
            buffer.put(flags)
            buffer.putShort(0) // Reserved
            buffer.putInt(payload.size)
            buffer.put(payload)
            return buffer.array()
        }

        fun readFromStream(input: InputStream): StreamPacket? {
            val header = ByteArray(HEADER_SIZE)
            var read = 0
            while (read < HEADER_SIZE) {
                val r = input.read(header, read, HEADER_SIZE - read)
                if (r == -1) return null
                read += r
            }

            if (header[0] != MAGIC[0] || header[1] != MAGIC[1] ||
                header[2] != MAGIC[2] || header[3] != MAGIC[3]
            ) {
                return null
            }

            val type = PacketType.fromByte(header[4])
            val flags = header[5]
            val payloadLen = ByteBuffer.wrap(header, 8, 4).order(ByteOrder.BIG_ENDIAN).int

            if (payloadLen < 0 || payloadLen > 50 * 1024 * 1024) return null

            val payload = ByteArray(payloadLen)
            var pRead = 0
            while (pRead < payloadLen) {
                val r = input.read(payload, pRead, payloadLen - pRead)
                if (r == -1) return null
                pRead += r
            }

            return StreamPacket(type, flags, payload)
        }
    }
}
