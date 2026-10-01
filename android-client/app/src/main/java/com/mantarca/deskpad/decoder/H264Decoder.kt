package com.mantarca.deskpad.decoder

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

class H264Decoder(private val surface: Surface) {
    private var codec: MediaCodec? = null
    private var isConfigured = false
    private var configuredWidth = 0
    private var configuredHeight = 0
    private val bufferInfo = MediaCodec.BufferInfo()

    @Synchronized
    fun init(width: Int = 1920, height: Int = 1080) {
        if (isConfigured && configuredWidth == width && configuredHeight == height) return

        releaseInternal()

        val candidates = linkedMapOf<String, MediaFormat>(
            "${width}x${height}" to formatOf(width, height),
            "1920x1088" to formatOf(1920, 1088),
            "1920x1080" to formatOf(1920, 1080),
            "1280x720" to formatOf(1280, 720)
        )
        for ((name, f) in candidates) {
            if (tryConfigure(f, name)) return
        }
        Log.e(TAG, "Decoder başlatılamadı ($width x $height)")
    }

    private fun formatOf(w: Int, h: Int): MediaFormat {
        val f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h)
        f.setInteger(MediaFormat.KEY_FRAME_RATE, 60)
        return f
    }

    private fun tryConfigure(format: MediaFormat, name: String): Boolean {
        var decoder: MediaCodec? = null
        return try {
            decoder = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            decoder.configure(format, surface, null, 0)
            decoder.start()
            codec = decoder
            isConfigured = true
            configuredWidth = format.getInteger(MediaFormat.KEY_WIDTH)
            configuredHeight = format.getInteger(MediaFormat.KEY_HEIGHT)
            Log.i(TAG, "MediaCodec başlatıldı [$name] ${configuredWidth}x${configuredHeight}")
            true
        } catch (e: Exception) {
            Log.e(TAG, "Decoder başlatma hatası [$name]: ${e.message}")
            try { decoder?.release() } catch (_: Exception) {}
            false
        }
    }

    @Synchronized
    fun decodeChunk(data: ByteArray, isKeyframe: Boolean) {
        val decoder = codec ?: return
        if (!isConfigured) return

        try {
            val nalUnits = parseNalUnits(data)
            if (nalUnits.isEmpty()) {
                feedSingleNal(decoder, data, isKeyframe)
            } else {
                for (nal in nalUnits) {
                    feedSingleNal(decoder, nal, isKeyframe)
                }
            }
            drainOutput(decoder)
        } catch (e: Throwable) {
            Log.e(TAG, "Decode hatası", e)
        }
    }

    private fun feedSingleNal(decoder: MediaCodec, data: ByteArray, isKeyframe: Boolean) {
        val inputIndex = decoder.dequeueInputBuffer(5000) // 5ms timeout
        if (inputIndex < 0) return

        val inputBuffer: ByteBuffer = decoder.getInputBuffer(inputIndex) ?: return
        inputBuffer.clear()
        inputBuffer.put(data)

        val nalType = getNalType(data)
        val flags = when {
            nalType == 7 || nalType == 8 -> MediaCodec.BUFFER_FLAG_CODEC_CONFIG // SPS or PPS
            isKeyframe || nalType == 5 -> MediaCodec.BUFFER_FLAG_KEY_FRAME      // IDR
            else -> 0
        }

        val pts = System.nanoTime() / 1000
        decoder.queueInputBuffer(inputIndex, 0, data.size, pts, flags)
    }

    private fun drainOutput(decoder: MediaCodec) {
        var outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
        while (outputIndex >= 0) {
            decoder.releaseOutputBuffer(outputIndex, true) // render to surface
            outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
        }
    }

    /**
     * Extract NAL type from data that may start with 00 00 00 01 or 00 00 01
     */
    private fun getNalType(data: ByteArray): Int {
        if (data.size < 5) return -1

        val offset = when {
            data.size >= 5 && data[0] == 0.toByte() && data[1] == 0.toByte() &&
                data[2] == 0.toByte() && data[3] == 1.toByte() -> 4
            data.size >= 4 && data[0] == 0.toByte() && data[1] == 0.toByte() &&
                data[2] == 1.toByte() -> 3
            else -> return -1
        }

        return (data[offset].toInt() and 0x1F)
    }

    /**
     * Parse Annex-B NAL units from a byte array.
     * Splits on 00 00 00 01 start codes.
     */
    private fun parseNalUnits(data: ByteArray): List<ByteArray> {
        val units = mutableListOf<ByteArray>()
        val startPositions = mutableListOf<Int>()

        var i = 0
        while (i <= data.size - 4) {
            if (data[i] == 0.toByte() && data[i + 1] == 0.toByte() &&
                data[i + 2] == 0.toByte() && data[i + 3] == 1.toByte()
            ) {
                startPositions.add(i)
                i += 4
            } else {
                i++
            }
        }

        if (startPositions.size < 2) return emptyList()

        for (j in 0 until startPositions.size) {
            val start = startPositions[j]
            val end = if (j + 1 < startPositions.size) startPositions[j + 1] else data.size
            units.add(data.copyOfRange(start, end))
        }

        return units
    }

    private fun releaseInternal() {
        try {
            codec?.stop()
            codec?.release()
        } catch (e: Exception) {
        } finally {
            codec = null
            isConfigured = false
            configuredWidth = 0
            configuredHeight = 0
        }
    }

    @Synchronized
    fun release() {
        releaseInternal()
    }

    companion object {
        private const val TAG = "H264Decoder"

        data class Size(val width: Int, val height: Int)

        /**
         * Tablet ekraninin en-boy oranini koruyarak, donanim cozucunun destekledigi
         * en buyuk cozunurlugu secer. Host'a bu boyut bildirilir ve decoder bu boyutta kurulur.
         */
        fun pickDecodeSize(screenW: Int, screenH: Int): Size {
            if (screenW <= 0 || screenH <= 0) return Size(1280, 720)
            return try {
                val info = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.firstOrNull { ci ->
                    !ci.isEncoder && ci.supportedTypes.any { it.equals(MediaFormat.MIMETYPE_VIDEO_AVC, true) }
                } ?: return Size(screenW, screenH)

                val vc = info.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC).videoCapabilities
                val maxW = vc.supportedWidths.upper
                val maxH = vc.supportedHeights.upper
                val alignW = maxOf(2, vc.widthAlignment)
                val alignH = maxOf(2, vc.heightAlignment)
                Log.i(TAG, "AVC destek: W=${vc.supportedWidths} H=${vc.supportedHeights} align=${alignW}x${alignH}")
                Log.i(TAG, "isSizeSupported 2000x1200=${vc.isSizeSupported(2000, 1200)} 1920x1200=${vc.isSizeSupported(1920, 1200)} 1920x1088=${vc.isSizeSupported(1920, 1088)}")

                val targetW = minOf(screenW, maxW)
                val targetH = minOf(screenH, maxH)

                // Ekranin en-boy oranini koruyarak yuksekligi azaltarak dene
                var h = (targetH / alignH) * alignH
                while (h >= 240) {
                    val w = (((h.toLong() * screenW) / screenH).toInt() / alignW) * alignW
                    if (w in 320..maxW && vc.isSizeSupported(w, h)) {
                        Log.i(TAG, "pickDecodeSize($screenW x $screenH) => ${w} x ${h}")
                        return Size(w, h)
                    }
                    h -= alignH
                }

                // Aspect korunmadiysa en buyuk desteklenen boyutu dene
                val w = (targetW / alignW) * alignW
                val hh = (targetH / alignH) * alignH
                if (vc.isSizeSupported(w, hh)) {
                    Log.i(TAG, "pickDecodeSize($screenW x $screenH) => ${w} x ${hh} (fallback)")
                    return Size(w, hh)
                }

                Log.w(TAG, "Desteklenen boyut bulunamadi, 1280x720 kullanilacak")
                Size(1280, 720)
            } catch (e: Throwable) {
                Log.e(TAG, "pickDecodeSize hatasi", e)
                Size(screenW, screenH)
            }
        }
    }
}
