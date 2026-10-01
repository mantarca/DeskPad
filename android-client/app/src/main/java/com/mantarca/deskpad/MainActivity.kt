package com.mantarca.deskpad

import android.os.Build
import android.os.Bundle
import android.view.*
import android.view.animation.AnimationUtils
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.mantarca.deskpad.decoder.H264Decoder
import com.mantarca.deskpad.network.StreamClient
import com.mantarca.deskpad.network.UdpDiscoveryClient
import kotlinx.coroutines.*

class MainActivity : AppCompatActivity(), SurfaceHolder.Callback {

    private lateinit var surfaceView: SurfaceView
    private lateinit var overlayLayout: LinearLayout
    private lateinit var tvStatus: TextView
    private lateinit var tvSubStatus: TextView
    private lateinit var pulseView: View

    private var decoder: H264Decoder? = null
    private var streamClient: StreamClient? = null
    private val udpClient = UdpDiscoveryClient()
    
    private var targetIp = "127.0.0.1"
    private var targetPort = 2828
    private var discoveryJob: Job? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        // Keep screen on
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        hideSystemUI()

        surfaceView = findViewById(R.id.surfaceView)
        overlayLayout = findViewById(R.id.overlayLayout)
        tvStatus = findViewById(R.id.tvStatus)
        tvSubStatus = findViewById(R.id.tvSubStatus)
        pulseView = findViewById(R.id.pulseView)

        surfaceView.holder.addCallback(this)
        
        startDiscovery()
    }

    private fun startDiscovery() {
        discoveryJob = CoroutineScope(Dispatchers.IO).launch {
            while (isActive) {
                // Sadece bağlantı yoksa WiFi ara, varsa aramaya gerek yok.
                val pc = udpClient.discoverPc()
                if (pc != null) {
                    targetIp = pc.ip
                    targetPort = pc.port
                    withContext(Dispatchers.Main) {
                        tvSubStatus.text = "Bulundu: ${pc.pcName} (Wi-Fi)\nBağlanılıyor..."
                    }
                } else {
                    targetIp = "127.0.0.1" // Fallback to USB
                    targetPort = 2828
                    withContext(Dispatchers.Main) {
                        tvSubStatus.text = "Wi-Fi PC bulunamadı. USB kablo bağlantısı aranıyor..."
                    }
                }
                delay(3000)
            }
        }
    }

    private fun hideSystemUI() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        val controller = WindowInsetsControllerCompat(window, window.decorView)
        controller.hide(WindowInsetsCompat.Type.systemBars())
        controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemUI()
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
        val surface = holder.surface
        decoder = H264Decoder(surface)

        val windowManager = windowManager
        val display = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) display else @Suppress("DEPRECATION") windowManager.defaultDisplay

        val screenWidth = resources.displayMetrics.widthPixels
        val screenHeight = resources.displayMetrics.heightPixels
        val refreshRate = display?.refreshRate?.toInt() ?: 60

        // Donanim cozucunun destekledigi en buyuk cozunurlugu sec; host bu boyutta kodlasin.
        val decodeSize = H264Decoder.pickDecodeSize(screenWidth, screenHeight)

        decoder?.init(decodeSize.width, decodeSize.height)

        streamClient = StreamClient(
            decoder!!, decodeSize.width, decodeSize.height, refreshRate,
            getTargetIp = { targetIp },
            getTargetPort = { targetPort }
        ) { isConnected, isStreaming, message ->
            tvStatus.text = message
            if (isStreaming) {
                overlayLayout.visibility = View.GONE
                pulseView.clearAnimation()
            } else {
                overlayLayout.visibility = View.VISIBLE
                if (pulseView.animation == null) {
                    pulseView.startAnimation(AnimationUtils.loadAnimation(this, R.anim.pulse))
                }
            }
        }

        streamClient?.start()
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        streamClient?.stop()
        streamClient = null
        decoder?.release()
        decoder = null
    }

    override fun onDestroy() {
        super.onDestroy()
        discoveryJob?.cancel()
        streamClient?.stop()
        decoder?.release()
    }
}
