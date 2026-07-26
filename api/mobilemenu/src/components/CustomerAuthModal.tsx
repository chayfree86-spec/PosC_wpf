import React, { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ShieldCheck, MessageSquare, Phone, User, CheckCircle2, RefreshCw, X } from 'lucide-react';
import { useCartStore, getCookie } from '../store/useCartStore';
import { PocketBaseService } from '../services/pocketbase';

export const CustomerAuthModal: React.FC = () => {
  const { isAuthModalOpen, setIsAuthModalOpen, setCustomer, tableNumber } = useCartStore();

  const [step, setStep] = useState<'details' | 'otp'>('details');
  const [mobile, setMobile] = useState('');
  const [name, setName] = useState('');
  const [otpVal, setOtpVal] = useState(['', '', '', '']);
  const [errorMsg, setErrorMsg] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [resendTimer, setResendTimer] = useState(0);
  const [showSuccess, setShowSuccess] = useState(false);

  useEffect(() => {
    try {
      const savedMobile = getCookie('pos_customer_mobile');
      const savedName = getCookie('pos_customer_name');
      if (savedMobile) setMobile(savedMobile);
      if (savedName) setName(savedName);
    } catch (e) {
      console.error('Error prefilling from cookies:', e);
    }
  }, [isAuthModalOpen]);

  const otpInputsRef = useRef<(HTMLInputElement | null)[]>([]);

  useEffect(() => {
    if (resendTimer > 0) {
      const interval = setInterval(() => {
        setResendTimer((t) => t - 1);
      }, 1000);
      return () => clearInterval(interval);
    }
  }, [resendTimer]);

  if (!isAuthModalOpen) return null;

  const handleSendOtp = async () => {
    // Strict Indian mobile number validation (10 digits starting with 6, 7, 8 or 9)
    if (!mobile || !/^[6-9]\d{9}$/.test(mobile)) {
      setErrorMsg('Please enter a valid 10-digit mobile number starting with 6, 7, 8, or 9.');
      return;
    }
    setErrorMsg('');
    setIsSending(true);

    try {
      // 1. Check if customer exists in POS DB
      const checkRes = await PocketBaseService.checkCustomer(mobile);
      
      if (checkRes.exists && checkRes.customer) {
        // If customer exists, bypass OTP and log in immediately!
        const custName = checkRes.customer.name || name || '';
        setCustomer({ name: custName, mobile });
        
        // Show success and close modal
        setShowSuccess(true);
        setIsSending(false);
        
        setTimeout(async () => {
          setIsAuthModalOpen(false);
          const { checkoutPending, setCheckoutPending, submitCurrentOrder } = useCartStore.getState();
          if (checkoutPending) {
            setCheckoutPending(false);
            try {
              await submitCurrentOrder();
            } catch (err) {
              console.error('Auto-checkout auth failed:', err);
            }
          }
          setShowSuccess(false);
        }, 1500);
        
        return;
      }
      
      // 2. If customer does not exist, BYPASS OTP: Register and log in immediately!
      const verifyRes = await PocketBaseService.verifyOtp(mobile, name, '0000');
      
      if (verifyRes.success && verifyRes.customer) {
        setShowSuccess(true);
        setIsSending(false);
        
        setTimeout(async () => {
          setCustomer({ name: verifyRes.customer?.name || name || undefined, mobile });
          setIsAuthModalOpen(false);
          
          const { checkoutPending, setCheckoutPending, submitCurrentOrder } = useCartStore.getState();
          if (checkoutPending) {
            setCheckoutPending(false);
            try {
              await submitCurrentOrder();
            } catch (err) {
              console.error('Auto-checkout auth failed:', err);
            }
          }
          setShowSuccess(false);
        }, 1500);
      } else {
        throw new Error(verifyRes.message || 'Failed to verify.');
      }
    } catch (err: any) {
      setIsSending(false);
      setErrorMsg(err.message || 'Failed to send verification code. Please try again.');
    }
  };

  const handleOtpChange = (index: number, val: string) => {
    if (val && !/^\d+$/.test(val)) return;
    const newOtp = [...otpVal];
    newOtp[index] = val;
    setOtpVal(newOtp);

    // Auto-focus next input box
    if (val && index < 3) {
      otpInputsRef.current[index + 1]?.focus();
    }
  };

  const handleOtpKeyDown = (index: number, e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && !otpVal[index] && index > 0) {
      otpInputsRef.current[index - 1]?.focus();
    }
  };

  const handleVerifyOtp = async () => {
    const enteredOtp = otpVal.join('');
    if (enteredOtp.length !== 4) {
      setErrorMsg('Please enter the 4-digit verification code.');
      return;
    }

    setIsSending(true);
    setErrorMsg('');

    try {
      // Verify OTP and save customer details on backend
      const verifyRes = await PocketBaseService.verifyOtp(mobile, name, enteredOtp);

      if (verifyRes.success && verifyRes.customer) {
        setShowSuccess(true);
        setIsSending(false);

        // Play sweet success sounds
        try {
          const audioCtx = new (window.AudioContext || (window as any).webkitAudioContext)();
          const osc1 = audioCtx.createOscillator();
          const osc2 = audioCtx.createOscillator();
          const gain = audioCtx.createGain();
          osc1.connect(gain);
          osc2.connect(gain);
          gain.connect(audioCtx.destination);

          osc1.frequency.setValueAtTime(523.25, audioCtx.currentTime); // C5
          osc2.frequency.setValueAtTime(659.25, audioCtx.currentTime); // E5
          gain.gain.setValueAtTime(0.05, audioCtx.currentTime);

          osc1.start();
          osc2.start();
          gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.3);
          osc1.stop(audioCtx.currentTime + 0.3);
          osc2.stop(audioCtx.currentTime + 0.3);
        } catch (_) {}

        // Complete login after 1.5 seconds success screen
        setTimeout(async () => {
          setCustomer({ name: verifyRes.customer?.name || name || undefined, mobile });
          setIsAuthModalOpen(false);
          
          const { checkoutPending, setCheckoutPending, submitCurrentOrder } = useCartStore.getState();
          if (checkoutPending) {
            setCheckoutPending(false);
            try {
              await submitCurrentOrder();
            } catch (err) {
              console.error('Auto-checkout auth failed:', err);
            }
          }

          // Reset state
          setStep('details');
          setMobile('');
          setName('');
          setOtpVal(['', '', '', '']);
          setShowSuccess(false);
        }, 1500);
      } else {
        setIsSending(false);
        setErrorMsg(verifyRes.message || 'Incorrect verification code. Please try again.');
      }
    } catch (err: any) {
      setIsSending(false);
      setErrorMsg(err.message || 'Verification failed. Please try again.');
    }
  };

  return (
    <AnimatePresence>
      <div className="fixed inset-0 z-50 flex items-center justify-center p-5 font-nunito">
        {/* Backdrop overlay */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={() => setIsAuthModalOpen(false)}
          className="absolute inset-0 bg-[#2E1513]/80 backdrop-blur-[6px]"
        />

        {/* Verification Card */}
        <motion.div
          initial={{ scale: 0.9, opacity: 0, y: 30 }}
          animate={{ scale: 1, opacity: 1, y: 0 }}
          exit={{ scale: 0.9, opacity: 0, y: 30 }}
          transition={{ type: 'spring', damping: 25, stiffness: 220 }}
          className="relative bg-[#FAF6F0] w-full max-w-sm rounded-[32px] overflow-hidden shadow-[0_24px_50px_rgba(46,21,19,0.3)] z-10 p-6 flex flex-col border border-[#F2ECE4]"
        >
          {/* Close button */}
          {!showSuccess && (
            <button
              onClick={() => setIsAuthModalOpen(false)}
              className="absolute top-4 right-4 z-20 bg-white hover:bg-[#FAF6F0] text-[#2E1513] border border-[#EFECE6] p-1.5 rounded-full shadow-sm active:scale-90 transition-all duration-200 cursor-pointer"
            >
              <X className="w-4 h-4 stroke-[2.5]" />
            </button>
          )}
          {showSuccess ? (
            /* SUCCESS VIEW */
            <motion.div
              initial={{ opacity: 0, scale: 0.8 }}
              animate={{ opacity: 1, scale: 1 }}
              className="flex flex-col items-center justify-center py-10 text-center space-y-4"
            >
              <div className="w-16 h-16 rounded-full bg-[#E6F4EA] text-[#16A34A] flex items-center justify-center shadow-sm">
                <CheckCircle2 className="w-10 h-10 stroke-[2.5]" />
              </div>
              <div className="space-y-1">
                <h3 className="text-[20px] font-black text-[#2E1513]">Verification Successful</h3>
                <p className="text-[13px] text-[#7D7067] font-semibold">
                  Welcome to Table {tableNumber.padStart(2, '0')}! Enjoy dining with us.
                </p>
              </div>
            </motion.div>
          ) : step === 'details' ? (
            /* STEP 1: MOBILE & NAME INPUT */
            <div className="space-y-5">
              {/* Header Title */}
              <div className="text-center space-y-1">
                <div className="w-11 h-11 bg-white border border-[#EFECE6] rounded-2xl flex items-center justify-center mx-auto mb-2 shadow-sm">
                  <ShieldCheck className="w-6 h-6 text-[#C27A3F] stroke-[2.2]" />
                </div>
                <h3 className="text-[19px] font-black text-[#2E1513]">Customer Verification</h3>
                <p className="text-[12.5px] text-[#7D7067] font-semibold max-w-[260px] mx-auto leading-normal">
                  Table {tableNumber.padStart(2, '0')} locked! Please verify your phone to confirm table orders.
                </p>
              </div>

              {/* Form Input fields */}
              <div className="space-y-3.5">
                {/* Mobile Number Input box */}
                <div className="space-y-1.5">
                  <label className="text-[11px] font-black uppercase tracking-widest text-[#7D7067] pl-1">
                    Mobile Number <span className="text-red-500">*</span>
                  </label>
                  <div className="relative flex items-center w-full">
                    <Phone className="absolute left-4 w-4.5 h-4.5 text-[#8E8075]" />
                    <span className="absolute left-11 text-[14.5px] font-black text-[#2E1513]">
                      +91
                    </span>
                    <input
                      type="tel"
                      value={mobile}
                      onChange={(e) => setMobile(e.target.value.replace(/\D/g, '').slice(0, 10))}
                      placeholder="Enter 10-Digit Mobile"
                      autoComplete="tel"
                      inputMode="tel"
                      pattern="[6-9][0-9]{9}"
                      className="w-full bg-white border border-[#EFECE6] rounded-2xl pl-20 pr-4 py-3.5 text-[14.5px] font-bold text-[#2E1513] placeholder-[#8E8075] focus:border-[#C27A3F] focus:ring-2 focus:ring-[#C27A3F]/15 outline-none shadow-sm transition-all"
                    />
                  </div>
                </div>

                {/* Name Optional input box */}
                <div className="space-y-1.5">
                  <label className="text-[11px] font-black uppercase tracking-widest text-[#7D7067] pl-1">
                    Your Name <span className="text-[#8E8075] text-[10px] font-bold">(Optional)</span>
                  </label>
                  <div className="relative flex items-center w-full">
                    <User className="absolute left-4 w-4.5 h-4.5 text-[#8E8075]" />
                    <input
                      type="text"
                      value={name}
                      onChange={(e) => setName(e.target.value)}
                      placeholder="Enter Your Name"
                      className="w-full bg-white border border-[#EFECE6] rounded-2xl pl-11 pr-4 py-3.5 text-[14.5px] font-bold text-[#2E1513] placeholder-[#8E8075] focus:border-[#C27A3F] focus:ring-2 focus:ring-[#C27A3F]/15 outline-none shadow-sm transition-all"
                    />
                  </div>
                </div>
              </div>

              {errorMsg && (
                <p className="text-[12px] font-bold text-red-700 bg-red-50 border border-red-100 px-3.5 py-2.5 rounded-xl animate-shake text-center">
                  {errorMsg}
                </p>
              )}

              {/* Submit CTA */}
              <button
                onClick={handleSendOtp}
                disabled={isSending}
                className="w-full bg-[#C27A3F] hover:bg-[#A6632D] text-white py-3.5 px-6 rounded-full font-black text-[14px] tracking-wide shadow-md active:scale-[0.98] transition-all duration-200 flex items-center justify-center gap-2 cursor-pointer disabled:opacity-75 disabled:pointer-events-none"
              >
                <ShieldCheck className="w-4.5 h-4.5 stroke-[2.5]" />
                {isSending ? 'Verifying...' : 'Verify & Proceed'}
              </button>
            </div>
          ) : (
            /* STEP 2: OTP VERIFICATION BOXES */
            <div className="space-y-5">
              {/* Header Title */}
              <div className="text-center space-y-1">
                <div className="w-11 h-11 bg-white border border-[#EFECE6] rounded-2xl flex items-center justify-center mx-auto mb-2 shadow-sm">
                  <MessageSquare className="w-6 h-6 text-[#25D366] stroke-[2.2]" />
                </div>
                <h3 className="text-[19px] font-black text-[#2E1513]">Enter WhatsApp OTP</h3>
                <p className="text-[12.5px] text-[#7D7067] font-semibold max-w-[260px] mx-auto leading-normal">
                  We sent a 4-digit verification code to <span className="text-[#2E1513] font-bold">+91 {mobile}</span>.
                </p>
              </div>

              {/* 4 single digit inputs */}
              <div className="flex justify-center gap-3 py-1">
                {otpVal.map((digit, idx) => (
                  <input
                    key={idx}
                    type="tel"
                    maxLength={1}
                    value={digit}
                    ref={(el) => { otpInputsRef.current[idx] = el; }}
                    onChange={(e) => handleOtpChange(idx, e.target.value)}
                    onKeyDown={(e) => handleOtpKeyDown(idx, e)}
                    className="w-12 h-14 bg-white border-2 border-[#EFECE6] focus:border-[#C27A3F] rounded-2xl text-center text-[22px] font-black text-[#2E1513] shadow-sm outline-none transition-all focus:ring-4 focus:ring-[#C27A3F]/10"
                  />
                ))}
              </div>

              {errorMsg && (
                <p className="text-[12px] font-bold text-red-700 bg-red-50 border border-red-100 px-3.5 py-2.5 rounded-xl animate-shake text-center">
                  {errorMsg}
                </p>
              )}

              {/* Verify & Resend actions */}
              <div className="space-y-3.5">
                <button
                  onClick={handleVerifyOtp}
                  className="w-full bg-[#2E1513] hover:bg-[#421F1C] text-white py-3.5 px-6 rounded-full font-black text-[14px] tracking-wide shadow-md active:scale-[0.98] transition-all duration-200 flex items-center justify-center gap-1.5 cursor-pointer"
                >
                  Verify Code & Enter
                </button>

                <div className="flex justify-center items-center gap-1.5 text-[12.5px] text-[#7D7067] font-bold">
                  {resendTimer > 0 ? (
                    <span className="flex items-center gap-1.5">
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      Resend code in {resendTimer}s
                    </span>
                  ) : (
                    <button
                      onClick={handleSendOtp}
                      className="text-[#C27A3F] hover:text-[#A6632D] underline active:scale-95 transition-transform cursor-pointer"
                    >
                      Resend Code via WhatsApp
                    </button>
                  )}
                </div>

                <div className="flex justify-center items-center pt-1 border-t border-[#F0EAE1] mt-2">
                  <button
                    onClick={() => {
                      setStep('details');
                      setOtpVal(['', '', '', '']);
                      setErrorMsg('');
                    }}
                    className="text-[12.5px] font-extrabold text-[#7D7067] hover:text-[#C27A3F] active:scale-95 transition-all cursor-pointer"
                  >
                    ← Edit Mobile Number
                  </button>
                </div>
              </div>
            </div>
          )}
        </motion.div>
      </div>
    </AnimatePresence>
  );
};
