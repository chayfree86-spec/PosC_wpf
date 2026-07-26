import React, { useState } from 'react';
import { ShoppingBag, Plus, Minus, CreditCard, ArrowRight, QrCode } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { SafeImage } from './SafeImage';

export const CheckoutDrawer: React.FC = () => {
  const {
    cart,
    tableNumber,
    updateQuantity,
    getCartTotal,
    setIsQrScannerOpen,
    customer,
    setIsAuthModalOpen,
    setCheckoutPending,
    isOrderingLoading,
    submitCurrentOrder,
    setActiveTab
  } = useCartStore();

  const [errorMsg, setErrorMsg] = useState('');

  const subtotal = getCartTotal();
  const grandTotal = subtotal;


  const handleCheckout = async () => {
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
    setErrorMsg('');
    try {
      await submitCurrentOrder();
    } catch (err) {
      console.error(err);
      setErrorMsg('Failed to send order to POS. Please try again.');
    }
  };

  if (cart.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] px-6 text-center font-nunito space-y-5 animate-[fadeIn_0.5s_ease-out]">
        <div className="w-20 h-20 bg-white border border-[#F4EFEA] rounded-full flex items-center justify-center shadow-[0_8px_24px_-4px_rgba(46,21,19,0.05)]">
          <ShoppingBag className="w-8 h-8 text-[#8E8075]" />
        </div>
        <div className="space-y-1.5">
          <h3 className="text-[19px] font-extrabold text-[#2E1513]">Your cart is empty</h3>
          <p className="text-[13.5px] text-[#7D7067] font-medium max-w-[260px] leading-relaxed mx-auto">
            Browse our artisan selections and add your favorite dishes.
          </p>
        </div>
        <button
          onClick={() => setActiveTab('menu')}
          className="bg-[#2E1513] text-white font-bold text-[14px] px-6 py-3 rounded-full shadow-md active:scale-95 transition-transform"
        >
          View Menu
        </button>
      </div>
    );
  }


  return (
    <div className="px-5 pb-32 font-nunito animate-[fadeIn_0.4s_ease-out]">
      <h2 className="text-[20px] font-extrabold text-[#2E1513] border-b border-[#F4EFEA] pb-3 mb-5">
        Review Your Order
      </h2>

      {/* Cart Items List */}
      <div className="space-y-4 mb-8">
        {cart.map((item) => {
          const itemKey = item.customizations && item.customizations.length > 0
            ? `${item.id}-${[...item.customizations].sort().join(',')}`
            : item.id;
          return (
            <div
              key={itemKey}
              className="flex gap-4 bg-white border border-[#FAF6F0] rounded-2xl p-3.5 shadow-[0_4px_16px_rgba(46,21,19,0.03)]"
            >
              {/* Image */}
              <div className="w-16 h-16 rounded-[14px] overflow-hidden flex-shrink-0 bg-[#2E1513]/5">
                <SafeImage src={item.image} alt={item.name} className="menu-fit-image" />
              </div>

              {/* Details */}
              <div className="flex-grow flex flex-col justify-between py-0.5">
                <div>
                  <h4 className="font-extrabold text-[14.5px] text-[#2E1513] leading-tight mb-0.5">
                    {item.name}
                  </h4>
                  {item.customizations && item.customizations.length > 0 && (
                    <p className="text-[11px] text-[#8D5524] font-bold">
                      {item.customizations.join(', ')}
                    </p>
                  )}
                  {item.specialInstructions && (
                    <p className="text-[11px] text-[#8E8075] italic leading-tight mt-1">
                      "{item.specialInstructions}"
                    </p>
                  )}
                </div>

                <div className="flex items-center justify-between mt-2.5">
                  <span className="font-extrabold text-[15px] text-[#2E1513]">
                    ₹{(item.price * item.quantity).toFixed(2)}
                  </span>

                  {/* Quantity Actions */}
                  <div className="flex items-center bg-[#FAF6F0] border border-[#EFECE6] rounded-full p-1 shadow-sm">
                    <button
                      onClick={() => updateQuantity(item.id, item.quantity - 1, itemKey)}
                      className="w-6 h-6 rounded-full flex items-center justify-center bg-white text-[#2E1513] border border-[#EFECE6] active:scale-75 transition-transform"
                    >
                      <Minus className="w-2.5 h-2.5 stroke-[3]" />
                    </button>
                    <span className="w-7 text-center font-bold text-[13px] text-[#2E1513]">
                      {item.quantity}
                    </span>
                    <button
                      onClick={() => updateQuantity(item.id, item.quantity + 1, itemKey)}
                      className="w-6 h-6 rounded-full flex items-center justify-center bg-white text-[#2E1513] border border-[#EFECE6] active:scale-75 transition-transform"
                    >
                      <Plus className="w-2.5 h-2.5 stroke-[3]" />
                    </button>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Active Table Number Display */}
      <div className="mb-6">
        {tableNumber ? (
          <div className="bg-[#E6F4EA] border border-[#A3CFBB] text-[#146c43] p-4.5 rounded-2.5xl flex flex-col items-center justify-center text-center shadow-sm space-y-1 animate-[fadeIn_0.3s_ease-out]">
            <span className="text-[10px] font-black uppercase tracking-widest text-[#146c43]/70">Active Dining Table</span>
            <span className="text-[26px] font-black text-[#146c43] font-nunito tracking-tight">Table {tableNumber.padStart(2, '0')}</span>
            <span className="text-[11px] font-semibold text-[#146c43]/90">Your order will be served directly to this table.</span>
          </div>
        ) : (
          <div className="bg-[#FFF3CD] border border-[#FFE69C] text-[#664d03] p-5 rounded-2.5xl flex flex-col items-center justify-center text-center shadow-[0_6px_20px_rgba(46,21,19,0.02)] space-y-3">
            <div className="w-10 h-10 rounded-full bg-[#C27A3F] text-white flex items-center justify-center shadow-md">
              <QrCode className="w-5 h-5 stroke-[2.5]" />
            </div>
            <div className="space-y-1">
              <h4 className="font-extrabold text-[14.5px] text-[#2E1513]">No Table Selected</h4>
              <p className="text-[12px] max-w-[220px] leading-relaxed mx-auto font-medium text-[#7D7067]">
                Please scan the QR code placed on your dining table to confirm and complete your order.
              </p>
            </div>
            <button
              onClick={() => setIsQrScannerOpen(true)}
              className="bg-[#2E1513] text-white font-black text-[12px] px-5 py-2.5 rounded-full shadow-md active:scale-95 transition-transform cursor-pointer flex items-center gap-1.5"
            >
              Scan Table QR
            </button>
          </div>
        )}

        {errorMsg && (
          <p className="text-[12px] font-bold text-[#b91c1c] bg-[#fef2f2] px-3 py-2 rounded-xl mt-3 animate-shake">
            {errorMsg}
          </p>
        )}
      </div>

      {/* Payment and Cost Receipt Summary */}
      <div className="bg-[#FAF6F0] border border-[#EFECE6] rounded-2.5xl p-5 space-y-3.5 mb-8 shadow-[inset_0_2px_4px_rgba(46,21,19,0.01)]">
        <h4 className="text-[12px] font-extrabold text-[#2E1513] tracking-widest uppercase border-b border-[#EFECE6] pb-2">
          Receipt Summary
        </h4>
        <div className="space-y-2">
          <div className="flex justify-between text-[14px] text-[#7D7067] font-medium">
            <span>Subtotal</span>
            <span className="font-bold text-[#2E1513]">₹{subtotal.toFixed(2)}</span>
          </div>
          <div className="h-px bg-[#EFECE6] my-1" />
          <div className="flex justify-between text-[17px] text-[#2E1513] font-extrabold">
            <span>Grand Total</span>
            <span>₹{grandTotal.toFixed(2)}</span>
          </div>
        </div>
      </div>

      {/* Checkout Submit to POS Button */}
      <button
        onClick={handleCheckout}
        disabled={isOrderingLoading}
        className="w-full bg-[#2E1513] hover:bg-[#421F1C] text-white py-4 px-6 rounded-full font-bold text-[15px] tracking-wide shadow-[0_8px_24px_rgba(46,21,19,0.12)] hover:shadow-[0_10px_28px_rgba(46,21,19,0.18)] active:scale-[0.98] transition-all duration-200 flex justify-between items-center disabled:opacity-75 disabled:pointer-events-none"
      >
        <span className="flex items-center gap-2">
          <CreditCard className="w-5 h-5 stroke-[2]" />
          {isOrderingLoading ? 'Sending to POS...' : 'Confirm & Order'}
        </span>
        <span className="bg-[#C27A3F] text-white font-extrabold px-3 py-1 rounded-full text-[12px] shadow-sm flex items-center gap-1.5">
          ₹{grandTotal.toFixed(2)}
          <ArrowRight className="w-3.5 h-3.5" />
        </span>
      </button>
    </div>
  );
};
