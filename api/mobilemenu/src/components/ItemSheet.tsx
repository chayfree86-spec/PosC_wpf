import React, { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X } from 'lucide-react';
import type { MenuItem } from '../services/pocketbase';
import { useCartStore } from '../store/useCartStore';
import { SafeImage } from './SafeImage';

interface ItemSheetProps {
  item: MenuItem | null;
  onClose: () => void;
}

export const ItemSheet: React.FC<ItemSheetProps> = ({ item, onClose }) => {
  const { addToCart, setActiveTab } = useCartStore();
  const [quantity, setQuantity] = useState(1);

  // Reset quantity when popup opens for a new item
  useEffect(() => {
    if (item) {
      setQuantity(1);
    }
  }, [item]);

  if (!item) return null;

  const handleAddToCart = () => {
    addToCart(
      {
        id: item.id,
        name: item.name,
        price: item.price,
        image: item.image
      },
      quantity,
      [],
      ''
    );
    onClose();
  };

  const handleOrderNow = () => {
    addToCart(
      {
        id: item.id,
        name: item.name,
        price: item.price,
        image: item.image
      },
      quantity,
      [],
      ''
    );
    setActiveTab('cart'); // Switch directly to checkout page
    onClose();
  };

  return (
    <AnimatePresence>
      <div className="fixed inset-0 z-50 flex items-end justify-center font-nunito">
        {/* Backdrop transparent blur overlay */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={onClose}
          className="absolute inset-0 bg-[#2E1513]/40 backdrop-blur-[3px]"
        />

        {/* Drawer popup Sheet */}
        <motion.div
          initial={{ y: '100%' }}
          animate={{ y: 0 }}
          exit={{ y: '100%' }}
          transition={{ type: 'spring', damping: 28, stiffness: 240 }}
          className="relative bg-[#FAF6F0] w-full max-w-md rounded-t-[32px] overflow-hidden shadow-[0_-12px_40px_rgba(46,21,19,0.18)] max-h-[85vh] flex flex-col z-10"
        >
          {/* Close circular button */}
          <button
            onClick={onClose}
            className="absolute top-4 right-4 z-20 bg-white/80 backdrop-blur-md text-[#2E1513] border border-[#EFECE6] p-2 rounded-full shadow-[0_4px_12px_rgba(46,21,19,0.06)] active:scale-90 transition-all duration-200"
          >
            <X className="w-4 h-4 stroke-[2.5]" />
          </button>

          {/* Scrollable Container (Compact) */}
          <div className="overflow-y-auto flex-grow pb-40">
            {/* Food Image */}
            <div className="w-full aspect-[16/10] relative overflow-hidden bg-[#2E1513]/5">
              <SafeImage
                src={item.image}
                alt={item.name}
                className="menu-fit-image"
                fallbackType={item.category === 'Artisan Tea' || item.category === 'Coffee' || item.category === 'Green Tea' || item.category === 'Summer' ? 'drink' : 'food'}
              />
              <div className="absolute inset-0 bg-gradient-to-t from-[#3B2018]/88 via-[#6F4A36]/34 to-[#1E120F]/22" />
            </div>

            {/* Product Details Section (Name & Price Only) */}
            <div className="px-6 pt-5">
              <div className="flex justify-between items-start gap-4">
                <h2 className="text-[24px] font-extrabold text-[#5A3526] leading-tight">
                  {item.name}
                </h2>
                <span className="text-[24px] font-black text-[#4A2A1E] whitespace-nowrap">
                  ₹{item.price.toFixed(0)}
                </span>
              </div>
            </div>
          </div>

          {/* Tactile Sticky Bottom Actions Panel (Super Simple for Village) */}
          <div className="absolute bottom-0 left-0 right-0 bg-white border-t border-[#F0EAE1] px-5 py-4.5 flex flex-col gap-4 z-20 shadow-[0_-4px_20px_rgba(46,21,19,0.04)]">
            {/* Quantity Selector row */}
            <div className="flex items-center justify-between">
              <span className="text-[13.5px] font-black text-[#7D7067] uppercase tracking-wider pl-1">
                Select Quantity
              </span>
              
              <div className="flex items-center bg-[#FAF6F0] border-2 border-[#EBE3D7] rounded-full p-0.5 shadow-sm font-nunito">
                <button
                  onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                  className="w-8 h-8 rounded-full flex items-center justify-center bg-white text-[#C27A3F] border border-[#EFECE6] active:scale-75 transition-all shadow-sm font-black text-[16px]"
                >
                  -
                </button>
                <span className="w-10 text-center font-black text-[16px] text-[#2E1513]">
                  {quantity}
                </span>
                <button
                  onClick={() => setQuantity((q) => q + 1)}
                  className="w-8 h-8 rounded-full flex items-center justify-center bg-[#C27A3F] text-white active:scale-75 transition-all shadow-sm font-black text-[16px]"
                >
                  +
                </button>
              </div>
            </div>

            {/* Action Buttons Row */}
            <div className="flex gap-3">
              {/* Add to Cart Button */}
              <button
                onClick={handleAddToCart}
                className="flex-1 bg-[#FAF6F0] hover:bg-[#E2D8CD] text-[#2E1513] border border-[#C27A3F] py-3.5 px-4 rounded-full font-black text-[14.5px] active:scale-[0.98] transition-all duration-200 text-center"
              >
                Add to Cart
              </button>

              {/* Order Now (Instantly checkout) Button */}
              <button
                onClick={handleOrderNow}
                className="flex-1 bg-[#2E1513] hover:bg-[#421F1C] text-white py-3.5 px-4 rounded-full font-black text-[14.5px] shadow-md active:scale-[0.98] transition-all duration-200 text-center flex justify-between items-center px-5"
              >
                <span>Order</span>
                <span className="bg-[#C27A3F] text-white font-black px-2.5 py-0.5 rounded-full text-[11.5px] shadow-sm">
                  ₹{(item.price * quantity).toFixed(0)}
                </span>
              </button>
            </div>
          </div>
        </motion.div>
      </div>
    </AnimatePresence>
  );
};
