import React, { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ArrowLeft } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import type { MenuItem } from '../services/pocketbase';
import { SafeImage } from './SafeImage';
import { sortCategories } from '../utils/categorySorter';

interface CategoryGridProps {
  onSelectItem: (item: MenuItem) => void;
  menuItems: MenuItem[];
}

export const CategoryGrid: React.FC<CategoryGridProps> = ({ onSelectItem, menuItems }) => {
  const { activeCategory, selectedSubcategory, setActiveCategory, isCategoryOpen, setIsCategoryOpen, cart, addToCart, updateQuantity } = useCartStore();
  const [viewMode, setViewMode] = useState<'categories' | 'items'>('categories');

  // Sync local viewMode with Zustand store state for automatic category switches
  React.useEffect(() => {
    setViewMode(isCategoryOpen ? 'items' : 'categories');
  }, [isCategoryOpen]);

  // Group unique categories and show only backend-provided images.
  const uniqueCategoryNames = sortCategories(Array.from(new Set(menuItems.map((item) => item.category))));
  const categories = uniqueCategoryNames.map((catName) => {
    const categoryItems = menuItems.filter((item) => item.category === catName);
    const matchedItem = categoryItems[0];
    const imageItem = categoryItems.find((item) => item.categoryImage || item.image || item.subcategoryImage);
    return {
      name: catName,
      tagline: matchedItem?.tagline || 'SELECTIONS',
      image: imageItem?.categoryImage || imageItem?.image || imageItem?.subcategoryImage || ''
    };
  });

  const sortByHighPrice = (items: MenuItem[]) => {
    return [...items].sort((a, b) => b.price - a.price || a.name.localeCompare(b.name));
  };

  const filteredItems = sortByHighPrice(menuItems.filter((item) => item.category === activeCategory));

  // Filter items based on active subcategory
  const itemsToShow = selectedSubcategory === 'All'
    ? filteredItems
    : sortByHighPrice(filteredItems.filter((item) => item.subcategory === selectedSubcategory));

  const handleCategoryClick = (categoryName: string) => {
    setActiveCategory(categoryName);
    setViewMode('items');
    setIsCategoryOpen(true);
  };

  // Variants for gorgeous springy transitions
  const containerVariants = {
    hidden: { opacity: 0 },
    show: {
      opacity: 1,
      transition: {
        staggerChildren: 0.08
      }
    }
  };

  const cardVariants = {
    hidden: { opacity: 0, y: 20 },
    show: { 
      opacity: 1, 
      y: 0,
      transition: { type: 'spring' as const, stiffness: 300, damping: 24 }
    }
  };

  return (
    <div className={`px-5 ${viewMode === 'items' ? 'pb-64' : 'pb-40'}`}>
      <AnimatePresence mode="wait">
        {viewMode === 'categories' ? (
          /* CATEGORY GRID VIEW */
          <motion.div
            key="categories-grid"
            variants={containerVariants}
            initial="hidden"
            animate="show"
            exit={{ opacity: 0, y: -10 }}
            className="grid grid-cols-2 gap-4 pt-2"
          >
            {categories.map((cat) => {
              const isColdDrink = cat.name.toLowerCase().includes('cold') || 
                                  cat.name.toLowerCase().includes('drink') || 
                                  cat.name.toLowerCase().includes('pepsi') || 
                                  cat.name.toLowerCase().includes('cola') || 
                                  cat.name.toLowerCase().includes('coldink');
              return (
                <motion.div
                  key={cat.name}
                  variants={cardVariants}
                  onClick={() => handleCategoryClick(cat.name)}
                  className={`group cursor-pointer bg-white border border-[#F2ECE4] rounded-[28px] p-3 flex flex-col shadow-[0_12px_24px_-8px_rgba(46,21,19,0.06)] active:scale-[0.98] transition-transform duration-200 ${
                    isColdDrink ? 'col-span-2' : ''
                  }`}
                >
                  {/* Image Area */}
                  <div className={`w-full ${isColdDrink ? 'aspect-[2/1]' : 'aspect-[4/3]'} rounded-[20px] overflow-hidden mb-3.5 relative bg-[#2E1513]/5`}>
                    <SafeImage
                      src={cat.image}
                      alt={cat.name}
                      className="menu-fit-image transition-transform duration-500 group-hover:scale-105"
                    />
                    <div className="absolute inset-0 bg-black/5 opacity-0 group-hover:opacity-100 transition-opacity" />
                  </div>

                  {/* Text Labels */}
                  <div className="px-1.5 pb-1.5 flex flex-col items-start">
                    <h3 className="font-extrabold text-[17px] text-[#2E1513] font-nunito tracking-tight mb-0.5 group-hover:text-[#C27A3F] transition-colors">
                      {cat.name}
                    </h3>
                  </div>
                </motion.div>
              );
            })}
          </motion.div>
        ) : (
          /* ITEMS LIST VIEW WITH BACK BUTTON */
          <motion.div
            key="items-list"
            initial={{ opacity: 0, x: 20 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: -20 }}
            transition={{ type: 'spring', damping: 25, stiffness: 200 }}
            className="space-y-5 pt-1"
          >
            {/* Navigation Header */}
            <div className="flex items-center justify-between border-b border-[#F4EFEA] pb-3.5">
              <button
                onClick={() => {
                  setViewMode('categories');
                  setIsCategoryOpen(false);
                }}
                className="flex items-center gap-2 text-[#7D7067] font-bold text-[14px] font-nunito active:scale-95 transition-transform"
              >
                <ArrowLeft className="w-4 h-4 text-[#2E1513]" />
                Back to Categories
              </button>
              <h2 className="text-[18px] font-extrabold text-[#2E1513] font-nunito">
                {activeCategory}
              </h2>
            </div>

            {/* Category Items List */}
            <div className="space-y-4">
              {itemsToShow.map((item) => {
                const cartItems = cart.filter(c => c.id === item.id);
                const cartQty = cartItems.reduce((acc, c) => acc + c.quantity, 0);
                const isAdded = cartQty > 0;

                return (
                  <div
                    key={item.id}
                    onClick={() => onSelectItem(item)}
                    className={`group relative flex gap-4 rounded-[24px] p-3 shadow-[0_8px_16px_-4px_rgba(46,21,19,0.03)] cursor-pointer transition-all duration-200 active:scale-[0.99] ${
                      isAdded
                        ? 'bg-[#FCF9F5] border-2 border-[#C27A3F]'
                        : 'bg-white border border-[#F4EFEA] hover:border-[#E2D8CD]'
                    }`}
                  >
                    {/* Dish Image */}
                    <div className="w-24 h-24 rounded-[18px] overflow-hidden flex-shrink-0 relative bg-[#2E1513]/5">
                      <SafeImage
                        src={item.image}
                        alt={item.name}
                        className="menu-fit-image group-hover:scale-102 transition-transform duration-300"
                        fallbackType={item.category === 'Artisan Tea' || item.category === 'Coffee' || item.category === 'Green Tea' || item.category === 'Summer' ? 'drink' : 'food'}
                      />
                      {/* Highly Obvious Tag for Selected/Added item */}
                      {isAdded && (
                        <div className="absolute top-1.5 left-1.5 bg-[#168A4A] text-white text-[9px] font-black px-2 py-0.5 rounded-full shadow-[0_4px_10px_rgba(22,138,74,0.28)] tracking-wider">
                          ✓ ADDED
                        </div>
                      )}
                    </div>

                    {/* Dish Content Details */}
                    <div className="flex flex-col justify-between py-1.5 flex-grow pr-1">
                      <div className="space-y-1">
                        <h4 className="font-extrabold text-[17px] text-[#2E1513] font-nunito leading-tight group-hover:text-[#C27A3F] transition-colors">
                          {item.name}
                        </h4>
                      </div>

                      <div className="flex items-center justify-between pt-1">
                        <span className="font-extrabold text-[17px] text-[#2E1513] font-nunito">
                          ₹{item.price.toFixed(2)}
                        </span>
                        
                        {/* Dynamic Card Quantity Controller & Add Button */}
                        {(() => {
                          if (cartQty === 0) {
                            return (
                              <button
                                className="bg-[#FAF6F0] hover:bg-[#C27A3F] text-[#2E1513] hover:text-white border border-[#C27A3F] px-4.5 py-1.5 rounded-full font-extrabold text-[12.5px] tracking-wider shadow-sm active:scale-90 transition-all duration-200"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  addToCart(
                                    { id: item.id, name: item.name, price: item.price, image: item.image },
                                    1,
                                    [],
                                    ''
                                  );
                                }}
                              >
                                ADD +
                              </button>
                            );
                          }

                          return (
                            <div 
                              className="flex items-center bg-[#FAF6F0] border-2 border-[#C27A3F] rounded-full p-0.5 shadow-sm font-nunito"
                              onClick={(e) => e.stopPropagation()}
                            >
                              <button
                                onClick={() => {
                                  const firstItem = cartItems[0];
                                  if (firstItem) {
                                    const key = firstItem.customizations && firstItem.customizations.length > 0
                                      ? `${firstItem.id}-${[...firstItem.customizations].sort().join(',')}`
                                      : firstItem.id;
                                    updateQuantity(firstItem.id, firstItem.quantity - 1, key);
                                  }
                                }}
                                className="w-7 h-7 rounded-full flex items-center justify-center bg-white text-[#C27A3F] border border-[#EFECE6] active:scale-75 transition-all shadow-sm font-black text-[15px]"
                              >
                                -
                              </button>
                              <span className="w-8 text-center font-black text-[15px] text-[#2E1513]">
                                {cartQty}
                              </span>
                              <button
                                onClick={() => {
                                  const firstItem = cartItems[0];
                                  if (firstItem) {
                                    const key = firstItem.customizations && firstItem.customizations.length > 0
                                      ? `${firstItem.id}-${[...firstItem.customizations].sort().join(',')}`
                                      : firstItem.id;
                                    updateQuantity(firstItem.id, firstItem.quantity + 1, key);
                                  } else {
                                    addToCart(
                                      { id: item.id, name: item.name, price: item.price, image: item.image },
                                      1,
                                      [],
                                      ''
                                    );
                                  }
                                }}
                                className="w-7 h-7 rounded-full flex items-center justify-center bg-[#C27A3F] text-white active:scale-75 transition-all shadow-sm font-black text-[15px]"
                              >
                                +
                              </button>
                            </div>
                          );
                        })()}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};
