import React, { useRef, useEffect } from 'react';
import { useCartStore } from '../store/useCartStore';
import type { MenuItem } from '../services/pocketbase';
import { sortCategories } from '../utils/categorySorter';

interface CategoryStripProps {
  menuItems: MenuItem[];
}

export const CategoryStrip: React.FC<CategoryStripProps> = ({ menuItems }) => {
  const { activeCategory, selectedSubcategory, setActiveCategory, setSelectedSubcategory, setIsCategoryOpen, getCartCount } = useCartStore();
  const hasCartItems = getCartCount() > 0;
  const scrollContainerRef = useRef<HTMLDivElement>(null);

  // Group unique categories
  const categories = sortCategories(Array.from(new Set(menuItems.map((item) => item.category))));
  const categoryItems = menuItems.filter((item) => item.category === activeCategory);
  const subcategories = Array.from(
    new Set(categoryItems.map((item) => item.subcategory).filter(Boolean))
  ) as string[];

  // Handle category switch
  const handleCategorySelect = (category: string) => {
    setActiveCategory(category);
    setIsCategoryOpen(true);
  };

  // Auto-scroll active item into view within the horizontal strip
  useEffect(() => {
    if (scrollContainerRef.current) {
      const activeElement = scrollContainerRef.current.querySelector('[data-active="true"]');
      if (activeElement) {
        activeElement.scrollIntoView({
          behavior: 'smooth',
          block: 'nearest',
          inline: 'center'
        });
      }
    }
  }, [activeCategory]);

  if (categories.length === 0) return null;

  return (
    <div className={`fixed ${hasCartItems ? 'bottom-[160px]' : 'bottom-[76px]'} left-0 right-0 max-w-[480px] mx-auto z-30 bg-[#FAF6F0]/94 backdrop-blur-md border-t border-[#F0EAE1] shadow-[0_-8px_24px_rgba(46,21,19,0.06)] font-nunito transition-all duration-300`}>
      {subcategories.length > 0 && (
        <div className="px-4 pt-2 pb-2 border-b border-[#F0EAE1]">
          <div className="flex items-center gap-3 mb-1.5">
            <div className="min-w-0">
              <span className="block text-[8px] font-black uppercase tracking-widest text-[#8E8075] leading-none">
                Subcategory of
              </span>
              <span className="block text-[13px] font-black text-[#2E1513] truncate mt-0.5">
                {activeCategory}
              </span>
            </div>
          </div>
          <div className="category-scroll flex gap-2 overflow-x-auto overflow-y-hidden pr-4">
            <button
              onClick={() => setSelectedSubcategory('All')}
              className={`px-3.5 py-1.5 rounded-full font-black text-[10.5px] uppercase tracking-wider transition-all duration-200 shrink-0 cursor-pointer border ${
                selectedSubcategory === 'All'
                  ? 'bg-[#168A4A] text-white border-[#168A4A] shadow-[0_4px_12px_rgba(22,138,74,0.18)]'
                  : 'bg-white border-[#EFECE6] text-[#7D7067]'
              }`}
            >
              All
            </button>
            {subcategories.map((subcat) => (
              <button
                key={subcat}
                onClick={() => setSelectedSubcategory(subcat)}
                className={`px-3.5 py-1.5 rounded-full font-black text-[10.5px] uppercase tracking-wider transition-all duration-200 shrink-0 cursor-pointer border ${
                  selectedSubcategory === subcat
                    ? 'bg-[#168A4A] text-white border-[#168A4A] shadow-[0_4px_12px_rgba(22,138,74,0.18)]'
                    : 'bg-white border-[#EFECE6] text-[#7D7067]'
                }`}
              >
                {subcat}
              </button>
            ))}
          </div>
        </div>
      )}
      <div className="px-4 pt-1.5 text-[8px] font-black uppercase tracking-widest text-[#8E8075]">
        Main Category
      </div>
      <div 
        ref={scrollContainerRef}
        className="category-scroll flex w-full min-w-0 items-center gap-2 overflow-x-auto overflow-y-hidden pt-1.5 pb-2.5 pl-4 pr-16"
      >
        {categories.map((cat) => {
          const isActive = activeCategory === cat;
          return (
            <button
              key={cat}
              data-active={isActive}
              onClick={() => handleCategorySelect(cat)}
              className={`px-3.5 py-1.5 rounded-full text-[12px] font-black tracking-tight whitespace-nowrap active:scale-95 transition-all duration-200 shadow-sm border ${
                isActive
                  ? 'bg-[#2E1513] text-white border-[#2E1513] shadow-[0_4px_12px_rgba(46,21,19,0.15)]'
                  : 'bg-white text-[#7D7067] border-[#EFECE6] hover:border-[#E2D8CD]'
              }`}
            >
              {cat}
            </button>
          );
        })}
      </div>
    </div>
  );
};
