import React from 'react';
import { UtensilsCrossed, Search, ShoppingBag, QrCode, ClipboardList } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';

export const BottomNav: React.FC = () => {
  const { activeTab, setActiveTab, getCartCount, orderStatus, setIsQrScannerOpen } = useCartStore();
  const cartCount = getCartCount();

  const handleTabClick = (tab: typeof activeTab) => {
    setActiveTab(tab);
  };

  return (
    <nav className="fixed bottom-0 left-0 right-0 z-40 bg-white border-t border-[#F0EAE1] pb-safe shadow-[0_-8px_30px_rgba(46,21,19,0.06)] flex items-center justify-around h-[76px] px-1 max-w-[480px] mx-auto rounded-t-[28px]">
      {/* 1. Menu Tab */}
      <button
        onClick={() => handleTabClick('menu')}
        className={`flex flex-col items-center justify-center w-14 h-full transition-all duration-300 active:scale-90 cursor-pointer ${
          activeTab === 'menu' ? 'text-[#C27A3F]' : 'text-[#8E8075]'
        }`}
      >
        <UtensilsCrossed className="w-[21px] h-[21px] stroke-[2.2]" />
        <span className={`text-[10px] font-black tracking-wider font-nunito uppercase mt-1 transition-all ${
          activeTab === 'menu' ? 'scale-100 font-black' : 'scale-95 font-bold opacity-80'
        }`}>
          Menu
        </span>
      </button>

      {/* 2. Search Tab */}
      <button
        onClick={() => handleTabClick('search')}
        className={`flex flex-col items-center justify-center w-14 h-full transition-all duration-300 active:scale-90 cursor-pointer ${
          activeTab === 'search' ? 'text-[#C27A3F]' : 'text-[#8E8075]'
        }`}
      >
        <Search className="w-[21px] h-[21px] stroke-[2.2]" />
        <span className={`text-[10px] font-black tracking-wider font-nunito uppercase mt-1 transition-all ${
          activeTab === 'search' ? 'scale-100 font-black' : 'scale-95 font-bold opacity-80'
        }`}>
          Search
        </span>
      </button>

      {/* 3. Center FAB (Order/QR Scanner) - Raising Above */}
      <div className="relative -top-5 flex flex-col items-center shrink-0 w-16">
        <button
          onClick={() => setIsQrScannerOpen(true)}
          className="w-[56px] h-[56px] rounded-full bg-[#C27A3F] hover:bg-[#A6632D] text-white flex items-center justify-center border-4 border-white shadow-[0_8px_20px_rgba(194,122,63,0.35)] active:scale-90 transition-all cursor-pointer"
          title="Scan Table QR Code"
        >
          <QrCode className="w-[23px] h-[23px] stroke-[2.5]" />
        </button>
        <span className="text-[10px] font-black tracking-wider font-nunito uppercase text-[#C27A3F] mt-1">
          Order
        </span>
      </div>

      {/* 4. Cart Tab */}
      <button
        onClick={() => handleTabClick(orderStatus !== 'none' ? 'status' : 'cart')}
        className={`relative flex flex-col items-center justify-center w-14 h-full transition-all duration-300 active:scale-90 cursor-pointer ${
          activeTab === 'cart' || activeTab === 'status' ? 'text-[#C27A3F]' : 'text-[#8E8075]'
        }`}
      >
        <ShoppingBag className="w-[21px] h-[21px] stroke-[2.2]" />
        <span className={`text-[10px] font-black tracking-wider font-nunito uppercase mt-1 transition-all ${
          activeTab === 'cart' || activeTab === 'status' ? 'scale-100 font-black' : 'scale-95 font-bold opacity-80'
        }`}>
          {orderStatus !== 'none' ? 'Status' : 'Cart'}
        </span>
        
        {/* Count Badge on Cart Icon */}
        {cartCount > 0 && orderStatus === 'none' && (
          <span className="absolute top-1.5 right-1.5 bg-[#2E1513] text-white text-[9px] font-black w-[17px] h-[17px] rounded-full flex items-center justify-center border border-white shadow-sm font-nunito animate-[pulse_2s_infinite]">
            {cartCount}
          </span>
        )}

        {/* Pulsing indicator if active order status */}
        {orderStatus !== 'none' && orderStatus !== 'served' && (
          <span className="absolute top-3 right-3 w-2 h-2 bg-[#C27A3F] rounded-full border border-white animate-ping" />
        )}
      </button>

      {/* 5. Report Tab */}
      <button
        onClick={() => handleTabClick('report')}
        className={`flex flex-col items-center justify-center w-14 h-full transition-all duration-300 active:scale-90 cursor-pointer ${
          activeTab === 'report' ? 'text-[#C27A3F]' : 'text-[#8E8075]'
        }`}
      >
        <ClipboardList className="w-[21px] h-[21px] stroke-[2.2]" />
        <span className={`text-[10px] font-black tracking-wider font-nunito uppercase mt-1 transition-all ${
          activeTab === 'report' ? 'scale-100 font-black' : 'scale-95 font-bold opacity-80'
        }`}>
          Report
        </span>
      </button>
    </nav>
  );
};
