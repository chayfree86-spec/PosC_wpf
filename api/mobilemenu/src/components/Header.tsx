import React, { useState } from 'react';
import { Coffee, CookingPot, Store, Download, X, FileText, ChevronsUpDown } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useCartStore } from '../store/useCartStore';
import type { MenuItem } from '../services/pocketbase';

interface HeaderProps {
  menuItems: MenuItem[];
}

export const Header: React.FC<HeaderProps> = ({ menuItems }) => {
  const tableNumber = useCartStore((state) => state.tableNumber);
  const isCategoryOpen = useCartStore((state) => state.isCategoryOpen);
  const activeTab = useCartStore((state) => state.activeTab);
  const businessInfo = useCartStore((state) => state.businessInfo);
  const selectedClient = useCartStore((state) => state.selectedClient);
  const setSelectedClient = useCartStore((state) => state.setSelectedClient);

  const switchRestaurant = () => {
    setSelectedClient(null);
    setTimeout(() => window.location.reload(), 60);
  };

  const [isDownloadModalOpen, setIsDownloadModalOpen] = useState(false);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);
  const [downloadSuccessIndex, setDownloadSuccessIndex] = useState<number | null>(null);
  const [isTextDownloading, setIsTextDownloading] = useState(false);

  const showGreeting = activeTab === 'menu' && !isCategoryOpen;

  const downloadFile = async (url: string, filename: string, index: number) => {
    setDownloadingIndex(index);
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error('Network response was not ok');
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = blobUrl;
      const ext = url.split('.').pop()?.split('?')[0] || 'png';
      a.download = `${filename.replace(/\s+/g, '_')}.${ext}`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(blobUrl);
      
      setDownloadSuccessIndex(index);
      setTimeout(() => setDownloadSuccessIndex(null), 2000);
    } catch (error) {
      console.error('Blob download failed, falling back to window.open:', error);
      window.open(url, '_blank', 'noopener,noreferrer');
    } finally {
      setDownloadingIndex(null);
    }
  };

  const downloadTextMenu = () => {
    setIsTextDownloading(true);
    try {
      const groupedMenu = menuItems.reduce<Record<string, MenuItem[]>>((groups, item) => {
        const category = item.category || 'Menu';
        groups[category] = groups[category] || [];
        groups[category].push(item);
        return groups;
      }, {});

      const body = Object.entries(groupedMenu).map(([category, items]) => {
        const lines = items.map((item) => {
          const description = item.description ? `\n  ${item.description}` : '';
          return `- ${item.name}  Rs. ${item.price.toFixed(2)}${description}`;
        });

        return `--- ${category.toUpperCase()} ---\n${lines.join('\n')}`;
      }).join('\n\n');

      const displayName = selectedClient?.name || businessInfo.name;
      const menuText = [
        '===================================================',
        `             ${displayName.toUpperCase()} - MENU`,
        '===================================================',
        '',
        body || 'Menu is currently unavailable.',
        '',
        '===================================================',
        '       Scan the QR code at your table to order!',
        '===================================================',
      ].join('\n');

      const blob = new Blob([menuText], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${displayName.replace(/[^a-z0-9]+/gi, '_') || 'Mobile'}_Menu.txt`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (e) {
      console.error(e);
    } finally {
      setIsTextDownloading(false);
    }
  };

  const handleDownloadMenu = () => {
    if (businessInfo.downloadImages && businessInfo.downloadImages.length > 0) {
      setIsDownloadModalOpen(true);
    } else {
      downloadTextMenu();
    }
  };

  return (
    <header className="pt-6 px-5 pb-2">
      {/* Brand Pill and Avatar Row */}
      <div className={`flex items-center justify-between gap-4 transition-all duration-300 ${showGreeting ? 'mb-8' : 'mb-3'}`}>
        <div className="flex items-center gap-2.5">
          {/* Circular Brand Logo (Dynamic Image or Fallback Icon) */}
          <div className="w-10 h-10 rounded-full overflow-hidden border border-[#EFECE6] bg-white flex items-center justify-center shadow-[0_4px_12px_rgba(46,21,19,0.06)]">
            {businessInfo.logoUrl ? (
              <img 
                src={businessInfo.logoUrl} 
                alt={selectedClient?.name || businessInfo.name} 
                className="menu-fit-image"
                onError={(e) => {
                  e.currentTarget.style.display = 'none';
                  const fallback = e.currentTarget.parentElement?.querySelector('.fallback-logo-icon');
                  if (fallback) fallback.classList.remove('hidden');
                }}
              />
            ) : null}
            <div className={`fallback-logo-icon ${businessInfo.logoUrl ? 'hidden' : 'flex'} items-center justify-center w-full h-full bg-white`}>
              {selectedClient?.slug === 'daalroti' ? (
                <CookingPot className="w-4.5 h-4.5 text-[#2E1513] stroke-[2.5]" />
              ) : selectedClient?.slug === 'chaychaupal' ? (
                <Coffee className="w-4.5 h-4.5 text-[#2E1513] stroke-[2.5]" />
              ) : (
                <Store className="w-4.5 h-4.5 text-[#2E1513] stroke-[2.5]" />
              )}
            </div>
          </div>

          {/* Business / Brand Name -> tap to switch restaurant */}
          <button
            type="button"
            onClick={switchRestaurant}
            title="Switch restaurant"
            className="bg-white border border-[#EFECE6] px-4 py-2 rounded-full shadow-[0_4px_16px_-4px_rgba(46,21,19,0.06)] flex items-center gap-1.5 active:scale-95 transition-transform"
          >
            <span className="font-black text-[13px] tracking-widest text-[#2E1513] font-nunito uppercase whitespace-nowrap">
              {selectedClient?.name || businessInfo.name}
            </span>
            <ChevronsUpDown className="w-3.5 h-3.5 text-[#C9BBAA] shrink-0" />
          </button>
        </div>

        
        {/* Download Menu Button (Only on Menu tab) */}
        {activeTab === 'menu' && (
          <button
            onClick={handleDownloadMenu}
            className="flex items-center gap-1.5 bg-[#FAF6F0] hover:bg-[#E2D8CD] text-[#2E1513] border border-[#C27A3F] px-3.5 py-2 rounded-full text-[11px] font-black active:scale-95 transition-all shadow-sm cursor-pointer whitespace-nowrap ml-auto"
            title="Download Menu"
          >
            <Download className="w-3.5 h-3.5 text-[#C27A3F] stroke-[2.5]" />
            <span>Download</span>
          </button>
        )}
        

      </div>

      {/* Greeting Title */}
      {showGreeting && (
        <div className="animate-[fadeIn_0.5s_ease-out] mb-1">
          <h1 
            style={{ fontSize: 'clamp(18px, 5.8vw, 26px)' }}
            className="font-extrabold text-[#2E1513] font-nunito tracking-tight leading-tight"
          >
            {tableNumber ? (
              <>
                Welcome back to <span className="text-[#16A34A] whitespace-nowrap">Table {tableNumber.padStart(2, '0')}</span>,
              </>
            ) : (
              'Welcome back,'
            )}
          </h1>
        </div>
      )}

      <AnimatePresence>
        {isDownloadModalOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-5 font-nunito">
            {/* Backdrop overlay */}
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setIsDownloadModalOpen(false)}
              className="absolute inset-0 bg-[#2E1513]/80 backdrop-blur-[6px]"
            />

            {/* Download Modal Card */}
            <motion.div
              initial={{ scale: 0.9, opacity: 0, y: 30 }}
              animate={{ scale: 1, opacity: 1, y: 0 }}
              exit={{ scale: 0.9, opacity: 0, y: 30 }}
              transition={{ type: 'spring', damping: 25, stiffness: 220 }}
              className="relative bg-[#FAF6F0] w-full max-w-sm rounded-[32px] overflow-hidden shadow-[0_24px_50px_rgba(46,21,19,0.3)] z-10 p-6 flex flex-col border border-[#F2ECE4]"
            >
              {/* Close Button */}
              <button
                onClick={() => setIsDownloadModalOpen(false)}
                className="absolute top-4 right-4 z-20 bg-white hover:bg-[#FAF6F0] text-[#2E1513] border border-[#EFECE6] p-1.5 rounded-full shadow-sm active:scale-90 transition-all duration-200 cursor-pointer"
              >
                <X className="w-4 h-4 stroke-[2.5]" />
              </button>

              {/* Title */}
              <div className="text-center space-y-1 mb-5">
                <div className="w-11 h-11 bg-white border border-[#EFECE6] rounded-2xl flex items-center justify-center mx-auto mb-2 shadow-sm">
                  <Download className="w-6 h-6 text-[#C27A3F] stroke-[2.2]" />
                </div>
                <h3 className="text-[19px] font-black text-[#2E1513]">Download Menu</h3>
                <p className="text-[12.5px] text-[#7D7067] font-semibold max-w-[260px] mx-auto leading-normal">
                  Select a menu file or page below to download.
                </p>
              </div>

              {/* Download Items Grid */}
              <div className="space-y-3 max-h-[300px] overflow-y-auto pr-1 no-scrollbar mb-4">
                {businessInfo.downloadImages.map((image, index) => {
                  const title = image.filename || `Menu Page ${index + 1}`;
                  const isDownloading = downloadingIndex === index;
                  const isSuccess = downloadSuccessIndex === index;
                  
                  return (
                    <div
                      key={index}
                      className="bg-white border border-[#EFECE6] rounded-2xl p-3 flex items-center justify-between gap-3 shadow-sm"
                    >
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="w-12 h-12 rounded-xl bg-[#FAF6F0] border border-[#EFECE6] flex items-center justify-center overflow-hidden flex-shrink-0">
                          {image.url ? (
                            <img src={image.url} alt={title} className="w-full h-full object-cover" />
                          ) : (
                            <FileText className="w-5 h-5 text-[#8E8075]" />
                          )}
                        </div>
                        <div className="min-w-0">
                          <span className="block text-[13px] font-black text-[#2E1513] truncate" title={title}>
                            {title}
                          </span>
                        </div>
                      </div>

                      <button
                        onClick={() => downloadFile(image.url, title, index)}
                        disabled={isDownloading}
                        className={`flex-shrink-0 px-4 py-2 rounded-full font-black text-[11px] uppercase tracking-wider transition-all duration-200 border cursor-pointer ${
                          isSuccess
                            ? 'bg-[#168A4A] text-white border-[#168A4A]'
                            : 'bg-[#C27A3F] hover:bg-[#A6632D] text-white border-[#C27A3F] shadow-sm'
                        }`}
                      >
                        {isDownloading ? '...' : isSuccess ? 'Done' : 'Download'}
                      </button>
                    </div>
                  );
                })}
              </div>

              {/* Text Menu fallback button */}
              <div className="pt-3 border-t border-[#F0EAE1] flex flex-col gap-2">
                <button
                  onClick={downloadTextMenu}
                  disabled={isTextDownloading}
                  className="w-full bg-[#2E1513] hover:bg-[#421F1C] text-white py-3 px-6 rounded-full font-black text-[12.5px] tracking-wider shadow-md active:scale-[0.98] transition-all duration-200 flex items-center justify-center gap-2 cursor-pointer"
                >
                  <FileText className="w-4 h-4 stroke-[2.2]" />
                  {isTextDownloading ? 'Downloading...' : 'Download Text Menu'}
                </button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </header>
  );
};
