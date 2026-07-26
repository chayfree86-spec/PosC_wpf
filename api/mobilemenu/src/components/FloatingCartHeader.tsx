import React, { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ShoppingBag, X, Plus, Minus, CreditCard, ChevronRight, Trash2 } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { SafeImage } from './SafeImage';

export const FloatingCartHeader: React.FC = () => {
  const {
    cart,
    updateQuantity,
    removeFromCart,
    getCartTotal,
    getCartCount,
    tableNumber,
    customer,
    setIsQrScannerOpen,
    setIsAuthModalOpen,
    setCheckoutPending,
    isOrderingLoading,
    submitCurrentOrder,
    activeTab
  } = useCartStore();

  const [isOpen, setIsOpen] = useState(false);

  // Only show if cart is not empty and user is on 'menu' or 'search' tab
  const showHeaderCard = (activeTab === 'menu' || activeTab === 'search') && getCartCount() > 0;

  if (!showHeaderCard) return null;

  const subtotal = getCartTotal();
  const grandTotal = subtotal;

  const handleDirectOrder = async () => {
    // Dismiss overlay first to make any scanner or auth modal clearly visible
    setIsOpen(false);

    // Case 1: If Table is not scanned yet, prompt QR scan
    if (!tableNumber) {
      setCheckoutPending(true);
      setIsQrScannerOpen(true);
      return;
    }

    // Case 2: Table is scanned, but customer is new (not verified yet)
    if (!customer) {
      setCheckoutPending(true);
      setIsAuthModalOpen(true);
      return;
    }

    // Case 3: Table and Customer both are verified, proceed directly
    try {
      await submitCurrentOrder();
    } catch (err) {
      console.error('Failed to submit order directly:', err);
      alert('Failed to place order. Please try again.');
    }
  };

  return (
    <>
      {/* 1. FLOATING FOOTER CARD (Fixed above BottomNav) */}
      <div className="px-5 fixed bottom-[88px] left-0 right-0 z-40 max-w-[480px] mx-auto w-full">
        <motion.div
          initial={{ y: 30, opacity: 0, scale: 0.95 }}
          animate={{ y: 0, opacity: 1, scale: 1 }}
          exit={{ y: 30, opacity: 0, scale: 0.95 }}
          transition={{ type: 'spring', damping: 20, stiffness: 250 }}
          onClick={() => setIsOpen(true)}
          className="bg-gradient-to-r from-[#168A4A] to-[#0F6836] text-white p-3.5 rounded-2xl shadow-[0_12px_28px_rgba(22,138,74,0.25)] border border-[#22C55E]/20 flex items-center justify-between cursor-pointer active:scale-[0.99] transition-transform duration-150"
        >
          <div className="flex items-center gap-3">
            <div className="relative">
              <div className="w-10 h-10 rounded-xl bg-white/15 border border-white/10 flex items-center justify-center shadow-md animate-pulse">
                <ShoppingBag className="w-5 h-5 text-white" />
              </div>
              <span className="absolute -top-1.5 -right-1.5 bg-[#FAF6F0] text-[#168A4A] text-[10px] font-black w-5 h-5 rounded-full border border-[#168A4A] flex items-center justify-center shadow-sm">
                {getCartCount()}
              </span>
            </div>
            
            <div className="font-nunito">
              <h5 className="text-[13.5px] font-black tracking-tight leading-tight">
                {getCartCount()} {getCartCount() === 1 ? 'Item' : 'Items'} Selected
              </h5>
              <p className="text-[11px] text-[#FAF6F0]/80 font-semibold mt-0.5">
                Tap to review selections
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5">
            <div className="text-right font-nunito">
              <span className="text-[10px] font-black uppercase tracking-wider text-[#86EFAC] block leading-none">Total</span>
              <span className="text-[16px] font-black text-[#FAF6F0]">₹{subtotal.toFixed(2)}</span>
            </div>
            <div className="w-7 h-7 rounded-lg bg-white/10 flex items-center justify-center text-white">
              <ChevronRight className="w-4 h-4 stroke-[3]" />
            </div>
          </div>
        </motion.div>
      </div>

      {/* 2. QUICK CART LIST OVERLAY (Bottom Drawer or Modal) */}
      <AnimatePresence>
        {isOpen && (
          <div className="fixed inset-0 z-50 flex items-end justify-center font-nunito">
            {/* Backdrop with blur */}
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setIsOpen(false)}
              className="absolute inset-0 bg-[#2E1513]/70 backdrop-blur-[4px]"
            />

            {/* Quick Cart Drawer */}
            <motion.div
              initial={{ y: '100%', opacity: 0.5 }}
              animate={{ y: 0, opacity: 1 }}
              exit={{ y: '100%', opacity: 0.5 }}
              transition={{ type: 'spring', damping: 26, stiffness: 220 }}
              className="relative bg-[#FAF6F0] w-full max-w-md rounded-t-[32px] overflow-hidden shadow-[0_-12px_40px_rgba(46,21,19,0.18)] z-10 p-5 flex flex-col border-t border-[#F2ECE4] max-h-[82vh]"
            >
              {/* Drag Handle indicator */}
              <div className="w-12 h-1.5 bg-[#EFECE6] rounded-full mx-auto mb-4" />

              {/* Header Row */}
              <div className="flex justify-between items-center border-b border-[#EFECE6] pb-3 mb-4.5">
                <div>
                  <h3 className="text-[18px] font-black text-[#2E1513] leading-tight">
                    Current Selections
                  </h3>
                  <p className="text-[12px] text-[#7D7067] font-semibold mt-0.5">
                    Review and modify items before ordering
                  </p>
                </div>
                <button
                  onClick={() => setIsOpen(false)}
                  className="w-8 h-8 rounded-full bg-[#FAF6F0] hover:bg-[#EFECE6] flex items-center justify-center text-[#2E1513] border border-[#EFECE6] transition-colors"
                >
                  <X className="w-4 h-4 stroke-[2.5]" />
                </button>
              </div>

              {/* Scrollable Item List */}
              <div className="overflow-y-auto space-y-3.5 mb-6 pr-1 max-h-[40vh] py-1">
                {cart.map((item) => {
                  const itemKey = item.customizations && item.customizations.length > 0
                    ? `${item.id}-${[...item.customizations].sort().join(',')}`
                    : item.id;
                  return (
                    <div
                      key={itemKey}
                      className="flex gap-3 bg-white border border-[#F2ECE4] rounded-2xl p-3 shadow-sm"
                    >
                      {/* Image */}
                      <div className="w-14 h-14 rounded-xl overflow-hidden flex-shrink-0 bg-[#2E1513]/5">
                        <SafeImage src={item.image} alt={item.name} className="menu-fit-image" />
                      </div>

                      {/* Details */}
                      <div className="flex-grow flex flex-col justify-between py-0.5">
                        <div className="relative pr-6">
                          <h4 className="font-extrabold text-[13.5px] text-[#2E1513] leading-tight mb-0.5">
                            {item.name}
                          </h4>
                          <button
                            onClick={() => removeFromCart(item.id, itemKey)}
                            className="absolute top-0 right-0 p-1 text-[#8E8075] hover:text-red-500 rounded-full hover:bg-red-50 transition-colors"
                            title="Delete item"
                          >
                            <Trash2 className="w-4 h-4 stroke-[2.5]" />
                          </button>
                          {item.customizations && item.customizations.length > 0 && (
                            <p className="text-[10px] text-[#C27A3F] font-black mt-0.5">
                              {item.customizations.join(', ')}
                            </p>
                          )}
                          {item.specialInstructions && (
                            <p className="text-[10px] text-[#8E8075] italic leading-tight mt-0.5">
                              "{item.specialInstructions}"
                            </p>
                          )}
                        </div>

                        <div className="flex items-center justify-between mt-2">
                          <span className="font-extrabold text-[14px] text-[#2E1513]">
                            ₹{(item.price * item.quantity).toFixed(2)}
                          </span>

                          {/* Plus/Minus Buttons */}
                          <div className="flex items-center bg-[#FAF6F0] border border-[#EFECE6] rounded-full p-0.5 shadow-sm">
                            <button
                              onClick={() => updateQuantity(item.id, item.quantity - 1, itemKey)}
                              className="w-5.5 h-5.5 rounded-full flex items-center justify-center bg-white text-[#2E1513] border border-[#EFECE6] active:scale-75 transition-transform"
                            >
                              <Minus className="w-2 h-2 stroke-[3]" />
                            </button>
                            <span className="w-6 text-center font-bold text-[12px] text-[#2E1513]">
                              {item.quantity}
                            </span>
                            <button
                              onClick={() => updateQuantity(item.id, item.quantity + 1, itemKey)}
                              className="w-5.5 h-5.5 rounded-full flex items-center justify-center bg-white text-[#2E1513] border border-[#EFECE6] active:scale-75 transition-transform"
                            >
                              <Plus className="w-2 h-2 stroke-[3]" />
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Receipt Summary block */}
              <div className="bg-white border border-[#EFECE6] rounded-2xl p-4.5 space-y-2 mb-6 shadow-sm">
                <div className="flex justify-between text-[13px] text-[#7D7067] font-semibold">
                  <span>Subtotal</span>
                  <span className="font-extrabold text-[#2E1513]">₹{subtotal.toFixed(2)}</span>
                </div>
                <div className="h-px bg-[#EFECE6] my-1" />
                <div className="flex justify-between text-[15.5px] text-[#2E1513] font-black">
                  <span>Grand Total</span>
                  <span>₹{grandTotal.toFixed(2)}</span>
                </div>
              </div>

              {/* Bottom Action buttons */}
              <div className="grid grid-cols-2 gap-3 pb-2">
                {/* Back / Continue Browsing button */}
                <button
                  onClick={() => setIsOpen(false)}
                  className="bg-[#FAF6F0] hover:bg-[#EFECE6] text-[#2E1513] font-extrabold text-[13.5px] py-3.5 rounded-full border border-[#C27A3F]/40 active:scale-95 transition-all shadow-sm cursor-pointer"
                >
                  Add More Items
                </button>

                {/* Direct Order button */}
                <button
                  onClick={handleDirectOrder}
                  disabled={isOrderingLoading}
                  className="bg-[#2E1513] hover:bg-[#421F1C] text-white font-extrabold text-[13.5px] py-3.5 rounded-full shadow-md active:scale-95 transition-all cursor-pointer flex items-center justify-center gap-1.5 disabled:opacity-70 disabled:pointer-events-none"
                >
                  <CreditCard className="w-4 h-4 stroke-[2.5]" />
                  <span>Direct Order</span>
                </button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
};
