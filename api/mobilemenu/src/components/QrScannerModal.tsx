import React, { useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X, QrCode } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { Html5Qrcode } from 'html5-qrcode';

interface QrScannerModalProps {
  onScanSuccess: (scannedText: string) => void;
}

export const QrScannerModal: React.FC<QrScannerModalProps> = ({ onScanSuccess }) => {
  const { isQrScannerOpen, setIsQrScannerOpen, setActiveTab } = useCartStore();
  const qrScannerRef = useRef<Html5Qrcode | null>(null);

  // Dynamically synthesize a crisp, premium audio "beep" sound offline
  const playBeep = () => {
    try {
      const audioCtx = new (window.AudioContext || (window as any).webkitAudioContext)();
      const oscillator = audioCtx.createOscillator();
      const gainNode = audioCtx.createGain();

      oscillator.connect(gainNode);
      gainNode.connect(audioCtx.destination);

      oscillator.type = 'sine';
      oscillator.frequency.setValueAtTime(1000, audioCtx.currentTime); // Sweet 1000Hz beep
      gainNode.gain.setValueAtTime(0.08, audioCtx.currentTime);

      oscillator.start();
      // Drop volume exponentially to create a clean, short beep
      gainNode.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.12);
      oscillator.stop(audioCtx.currentTime + 0.12);
    } catch (err) {
      console.log('Web Audio blocked or unsupported:', err);
    }
  };

  // Safe close handler that stops the scanner before unmounting to prevent crash
  const handleClose = async () => {
    if (qrScannerRef.current) {
      const scanner = qrScannerRef.current;
      if (scanner.isScanning) {
        try {
          await scanner.stop();
          scanner.clear();
        } catch (err) {
          console.error("Error stopping QR scanner on close:", err);
        }
      }
      qrScannerRef.current = null;
    }
    setActiveTab('menu');
    setIsQrScannerOpen(false);
  };

  // Initialize and stop the HTML5 QR Code Scanner
  useEffect(() => {
    if (!isQrScannerOpen) {
      // Clean up scanner if closed manually
      if (qrScannerRef.current) {
        if (qrScannerRef.current.isScanning) {
          qrScannerRef.current.stop().then(() => {
            qrScannerRef.current?.clear();
            qrScannerRef.current = null;
          }).catch(err => console.error("Error stopping QR scanner:", err));
        } else {
          qrScannerRef.current = null;
        }
      }
      return;
    }

    // Start scanner
    const startScanner = async () => {
      try {
        const scanner = new Html5Qrcode("qr-reader");
        qrScannerRef.current = scanner;

        await scanner.start(
          { facingMode: "environment" },
          {
            fps: 15,
            qrbox: (width, height) => {
              // Create a dynamic scanning box size for mobile screens (approx 70% of min dimension)
              const minDimension = Math.min(width, height);
              const size = Math.floor(minDimension * 0.7);
              return { width: size, height: size };
            }
          },
          async (decodedText) => {
            playBeep();
            
            // Stop scanning immediately on success
            try {
              await scanner.stop();
              scanner.clear();
              qrScannerRef.current = null;
            } catch (e) {
              console.error("Error stopping scanner on success:", e);
            }

            onScanSuccess(decodedText);
            setIsQrScannerOpen(false);
          },
          () => {
            // Ignore scan errors (fired on every frame if no QR is detected)
          }
        );

        // Auto-zoom the camera track if supported by the device browser
        setTimeout(async () => {
          try {
            const videoEl = document.querySelector('#qr-reader video') as HTMLVideoElement;
            if (videoEl && videoEl.srcObject) {
              const stream = videoEl.srcObject as MediaStream;
              const track = stream.getVideoTracks()[0];
              if (track && typeof track.getCapabilities === 'function') {
                const capabilities = track.getCapabilities() as any;
                if (capabilities.zoom) {
                  const max = capabilities.zoom.max || 1;
                  // Auto zoom to 2.0x (or max if max is less than 2.0)
                  const targetZoom = Math.min(2.0, max);
                  await track.applyConstraints({
                    advanced: [{ zoom: targetZoom }]
                  } as any);
                  console.log(`Auto-zoomed camera to: ${targetZoom}x`);
                }
              }
            }
          } catch (zoomErr) {
            console.warn('Auto-zoom constraints failed to apply:', zoomErr);
          }
        }, 500);
      } catch (err) {
        console.error("Failed to start QR scanner:", err);
      }
    };

    // Delay start slightly to let the modal transition finish
    const startTimer = setTimeout(startScanner, 250);

    return () => {
      clearTimeout(startTimer);
      if (qrScannerRef.current) {
        const scanner = qrScannerRef.current;
        if (scanner.isScanning) {
          scanner.stop().then(() => {
            scanner.clear();
          }).catch(err => console.error("Error stopping scanner in cleanup:", err));
        }
      }
    };
  }, [isQrScannerOpen, onScanSuccess, setIsQrScannerOpen]);

  if (!isQrScannerOpen) return null;

  return (
    <AnimatePresence>
      <div className="fixed inset-0 z-50 bg-black font-nunito overflow-hidden">
        {/* Style overrides to force video element to fill viewport */}
        <style>{`
          #qr-reader {
            width: 100% !important;
            height: 100% !important;
            border: none !important;
          }
          #qr-reader video {
            width: 100% !important;
            height: 100% !important;
            object-fit: cover !important;
          }
          #qr-reader div, #qr-reader canvas {
            border: none !important;
            box-shadow: none !important;
          }
        `}</style>

        {/* HTML5 QR Code Container (takes full screen) */}
        <div id="qr-reader" className="absolute inset-0 w-full h-full z-0 bg-black" />

        {/* Semi-transparent Darkened Mask Overlay with cut-out in center */}
        <div className="absolute inset-0 z-10 flex flex-col justify-between items-center py-12 pointer-events-none">
          {/* Header Title Layer */}
          <div className="w-full text-center px-6 mt-6 select-none drop-shadow-[0_2px_8px_rgba(0,0,0,0.8)]">
            <h3 className="text-[20px] font-black text-white tracking-wide">Scan Table QR Code</h3>
            <p className="text-[13px] text-white/85 font-semibold mt-1">
              Align the QR code on your table inside the frame
            </p>
          </div>

          {/* Viewfinder Center Box with thick gold frame and darkened backdrop */}
          <div 
            className="w-64 h-64 border-[3px] border-solid border-[#C27A3F] rounded-[32px] relative flex items-center justify-center pointer-events-auto"
            style={{
              boxShadow: '0 0 0 9999px rgba(0, 0, 0, 0.65)',
            }}
          >
            {/* Corner Bracket Accent highlights */}
            <div className="absolute -top-[3px] -left-[3px] w-6 h-6 border-t-[4px] border-l-[4px] border-white rounded-tl-[32px] pointer-events-none" />
            <div className="absolute -top-[3px] -right-[3px] w-6 h-6 border-t-[4px] border-r-[4px] border-white rounded-tr-[32px] pointer-events-none" />
            <div className="absolute -bottom-[3px] -left-[3px] w-6 h-6 border-b-[4px] border-l-[4px] border-white rounded-bl-[32px] pointer-events-none" />
            <div className="absolute -bottom-[3px] -right-[3px] w-6 h-6 border-b-[4px] border-r-[4px] border-white rounded-br-[32px] pointer-events-none" />

            {/* Glowing gold laser line sliding vertically */}
            <motion.div
              animate={{ 
                top: ['0%', '100%', '0%'] 
              }}
              transition={{ 
                duration: 2.2, 
                repeat: Infinity, 
                ease: 'easeInOut' 
              }}
              className="absolute left-0 right-0 h-1 bg-[#C27A3F] shadow-[0_0_15px_#C27A3F] z-20 pointer-events-none"
            />
            
            {/* Background vector icon */}
            <QrCode className="w-24 h-24 text-white/5 stroke-[1.2] pointer-events-none" />
          </div>

          {/* Bottom helper message */}
          <div className="px-8 text-center select-none drop-shadow-[0_2px_6px_rgba(0,0,0,0.8)]">
            <span className="text-[12px] bg-black/40 text-white/90 font-bold px-4 py-2 rounded-full border border-white/10 backdrop-blur-sm">
              Auto-focus & zoom enabled
            </span>
          </div>
        </div>

        {/* Circular Close Button over everything */}
        <button
          onClick={handleClose}
          className="absolute top-6 right-6 z-30 bg-black/40 hover:bg-black/60 text-white border border-white/20 p-3.5 rounded-full shadow-[0_4px_16px_rgba(0,0,0,0.4)] backdrop-blur-md active:scale-90 transition-all cursor-pointer"
        >
          <X className="w-5 h-5 stroke-[2.5]" />
        </button>
      </div>
    </AnimatePresence>
  );
};
