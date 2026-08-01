import React, { useEffect, useState } from 'react';
import { useCartStore } from './store/useCartStore';
import { Header } from './components/Header';
import { CategoryGrid } from './components/CategoryGrid';
import { Search } from './components/Search';
import { CheckoutDrawer } from './components/CheckoutDrawer';
import { OrderStatus } from './components/OrderStatus';
import { BottomNav } from './components/BottomNav';
import { ItemSheet } from './components/ItemSheet';
import { CategoryStrip } from './components/CategoryStrip';
import { QrScannerModal } from './components/QrScannerModal';
import { Report } from './components/Report';
import { CustomerAuthModal } from './components/CustomerAuthModal';
import { PocketBaseService, type MenuItem } from './services/pocketbase';
import { FloatingCartHeader } from './components/FloatingCartHeader';
import { ClientSelection } from './components/ClientSelection';
import { QrCode } from 'lucide-react';

const extractToken = (scannedText: string): string => {
  try {
    // 1. Search for 32-character hex token (e.g. 1087f905d1c17b49b450164630c9d6f8)
    const hex32Match = scannedText.match(/\b([a-fA-F0-9]{32})\b/);
    if (hex32Match) {
      return hex32Match[1];
    }

    // 2. Parse common URL parameters as a fallback
    if (scannedText.startsWith('http://') || scannedText.startsWith('https://')) {
      const url = new URL(scannedText);
      return (
        url.searchParams.get('table') ||
        url.searchParams.get('t') ||
        url.searchParams.get('token') ||
        url.searchParams.get('qr_token') ||
        url.searchParams.get('qr_code') ||
        scannedText
      );
    }
  } catch (e) {
    console.error('Failed to parse scanned URL:', e);
  }
  return scannedText;
};

export const App: React.FC = () => {
  const { activeTab, tableNumber, setTableNumber, isCategoryOpen, businessInfo, selectedClient, setIsQrScannerOpen } = useCartStore();
  const currentOrderId = useCartStore((s) => s.currentOrderId);
  const [selectedItem, setSelectedItem] = useState<MenuItem | null>(null);
  const [menuItems, setMenuItems] = useState<MenuItem[]>([]);
  const [loadingMenu, setLoadingMenu] = useState(true);
  const [menuLoadError, setMenuLoadError] = useState('');
  const [showQrToast, setShowQrToast] = useState(false);

  // Load menu items and business configuration from the POS backend on mount.
  const initAppData = async () => {
    setLoadingMenu(true);
    setMenuLoadError('');
    try {
      const data = await PocketBaseService.getMenu();
      setMenuItems(data);
      if (data.length > 0) {
        useCartStore.getState().setActiveCategory(data[0].category);
      }

      const businessData = await PocketBaseService.getBusinessInfo();
      useCartStore.getState().setBusinessInfo(businessData);
    } catch (err) {
      console.error('Error initializing app data:', err);
      setMenuItems([]);
      setMenuLoadError('Menu backend se data load nahi ho pa raha hai.');
    } finally {
      setLoadingMenu(false);
    }
  };

  useEffect(() => {
    initAppData();
  }, []);

  const retryMenuLoad = () => {
    initAppData();
  };

  // QR Scan Table Detection on mount
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const tableParam = params.get('table') || params.get('t') || params.get('token') || params.get('qr_token') || params.get('qr_code');
    
    if (tableParam) {
      const token = extractToken(tableParam);
      const store = useCartStore.getState();
      if (store.tableId !== token) {
        store.setCurrentOrderId(null);
        store.setOrderStatus('none');
      }

      PocketBaseService.validateQr(token).then((res) => {
        if (res && res.valid && res.table) {
          setTableNumber(res.table.table_number);
          store.setTableId(token);
          
          // Always call the API to verify customer check
          store.verifyCustomerOnScan().then((success) => {
            if (success) {
              setShowQrToast(true);
              const timer = setTimeout(() => {
                setShowQrToast(false);
              }, 5000);
              return () => clearTimeout(timer);
            }
          });
        } else {
          console.error('Invalid table token on mount:', token);
        }
      }).catch((err) => {
        console.error('Failed to validate QR token on mount:', err);
        let errDetail = '';
        try {
          errDetail = err instanceof Error ? `${err.name}: ${err.message}\n${err.stack || ''}` : JSON.stringify(err, Object.getOwnPropertyNames(err || {}), 2);
        } catch (_) {
          errDetail = String(err);
        }
        alert(`Failed to validate QR token on mount:\n${errDetail}`);
      });
    }
  }, [setTableNumber]);

  // App-wide watcher: once the POS settles the active order (status 'served')
  // -- or it's rejected -- free the table on the PWA. Runs on ANY screen (not
  // only the status tab) and drops the table token from the URL so a reload
  // can't re-select the now-finished table.
  useEffect(() => {
    if (!currentOrderId) return;
    const unsub = PocketBaseService.subscribeToOrderUpdates(currentOrderId, (o) => {
      const s = useCartStore.getState();
      s.setOrderStatus(o.status);
      if (o.status === 'served' || o.status === 'rejected') {
        s.setTableNumber('');
        s.setTableId('');
        try { window.history.replaceState(null, '', window.location.pathname); } catch (_) { /* ignore */ }
      }
    });
    return unsub;
  }, [currentOrderId]);

  const handleQrScanSuccess = async (scannedText: string) => {
    // 1. Try to extract client from URL and save it to store state
    try {
      if (scannedText.startsWith('http://') || scannedText.startsWith('https://')) {
        const url = new URL(scannedText);
        const clientParam = url.searchParams.get('client') || url.searchParams.get('pos_client');
        const token = extractToken(scannedText);
        
        if (clientParam) {
          const slug = clientParam.toLowerCase().replace(/[^a-z0-9_-]/g, '');
          const store = useCartStore.getState();
          
          // Case A: Customer is already on the menu page of the matching restaurant
          if (store.selectedClient && store.selectedClient.slug === slug) {
            // Close scanner modal
            setIsQrScannerOpen(false);
            
            // Validate table QR token
            const res = await PocketBaseService.validateQr(token);
            if (res && res.valid && res.table) {
              setTableNumber(res.table.table_number);
              
              // Clear active order only if it's a different table
              if (store.tableId !== token) {
                store.setCurrentOrderId(null);
                store.setOrderStatus('none');
              }
              
              store.setTableId(token);
              
              // Verify customer check
              await store.verifyCustomerOnScan();
              
              // Show table detection toast
              setShowQrToast(true);
              setTimeout(() => setShowQrToast(false), 5000);
            }
            return;
          }
          
          // Case B: Client is different, or no client is selected yet
          // Close scanner modal
          setIsQrScannerOpen(false);

          // Validate table QR token so table info is saved before switching
          const res = await PocketBaseService.validateQr(token);
          if (res && res.valid && res.table) {
            setTableNumber(res.table.table_number);
            store.setTableId(token);
          }

          // Clear active order since we scanned a new table
          store.setCurrentOrderId(null);
          store.setOrderStatus('none');

          // If a client was already selected, reset it to null first to show ClientSelection screen
          if (store.selectedClient) {
            store.setSelectedClient(null);
          }

          // Trigger the beautiful auto-highlight & redirect flow on ClientSelection screen.
          // ClientSelection matches this slug against the list it loaded from the backend,
          // so the name it shows is the server's, not one guessed from the URL.
          store.setAutoSelectClient({ slug });
          return;
        }
      }
    } catch (e) {
      console.error('Failed to auto-route in-app from scanned URL:', e);
    }

    // 2. Fallback: If it's a raw token and we already have selectedClient, validate it normally
    const token = extractToken(scannedText);
    try {
      const res = await PocketBaseService.validateQr(token);
      if (res && res.valid && res.table) {
        setTableNumber(res.table.table_number);
        useCartStore.getState().setTableId(token);
        
        const { checkoutPending, setCheckoutPending, submitCurrentOrder, verifyCustomerOnScan } = useCartStore.getState();
        const success = await verifyCustomerOnScan();
        if (success) {
          setShowQrToast(true);
          setTimeout(() => {
            setShowQrToast(false);
          }, 5000);
          
          if (checkoutPending) {
            setCheckoutPending(false);
            try {
              await submitCurrentOrder();
            } catch (err) {
              console.error('Auto-checkout scan failed:', err);
            }
          }
        }
      } else {
        alert('अमान्य क्यूआर कोड। कृपया अपनी टेबल पर लगा सही क्यूआर कोड स्कैन करें।');
      }
    } catch (err) {
      console.error('QR validation error:', err);
      let errDetail = '';
      try {
        errDetail = err instanceof Error ? `${err.name}: ${err.message}\n${err.stack || ''}` : JSON.stringify(err, Object.getOwnPropertyNames(err || {}), 2);
      } catch (_) {
        errDetail = String(err);
      }
      alert(`क्यूआर कोड वैलिडेट करने में त्रुटि हुई (QR Validation Failure):\n${errDetail}\n\nकृपया दोबारा प्रयास करें।`);
    }
  };

  const handleSelectItem = (item: MenuItem) => {
    setSelectedItem(item);
  };

  // First run (or after switching): make the customer pick a restaurant. Its
  // client context then scopes the menu and routes the order to that POS.
  if (!selectedClient) {
    return (
      <>
        <ClientSelection />
        <QrScannerModal onScanSuccess={handleQrScanSuccess} />
      </>
    );
  }

  return (
    <div className="bg-[#FAF6F0] min-h-screen selection:bg-[#2E1513]/10">
      <div className="mobile-viewport">
        {/* Automatic Table Detection Float Toast */}
        {showQrToast && tableNumber && (
          <div className="fixed top-4 left-4 right-4 z-50 mx-auto max-w-sm font-nunito animate-[slideDown_0.4s_cubic-bezier(0.16,1,0.3,1)_forwards]">
            <div className="bg-[#2E1513] text-white px-4 py-3 rounded-2xl flex items-center justify-between shadow-[0_12px_28px_rgba(46,21,19,0.22)] border border-white/10">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-full bg-[#C27A3F] flex items-center justify-center">
                  <QrCode className="w-4 h-4 text-white" />
                </div>
                <div>
                  <h5 className="text-[13px] font-black leading-none">Table Detected</h5>
                  <p className="text-[11px] text-[#EFECE6] font-medium mt-0.5">
                    Order will be sent to Table {tableNumber.padStart(2, '0')}
                  </p>
                </div>
              </div>
              <button
                onClick={() => setShowQrToast(false)}
                className="text-[11px] font-extrabold bg-white/10 hover:bg-white/20 px-2.5 py-1 rounded-lg"
              >
                Okay
              </button>
            </div>
          </div>
        )}

        {/* Brand Header */}
        <Header menuItems={menuItems} />

        {/* Loading Menu Spinner Overlay */}
        {loadingMenu ? (
          <div className="flex flex-col items-center justify-center min-h-[50vh] space-y-3 font-nunito">
            <div className="w-10 h-10 border-4 border-[#C27A3F] border-t-transparent rounded-full animate-spin" />
            <p className="text-[13px] font-bold text-[#8E8075]">
              Setting up {selectedClient?.name || businessInfo.name} menu...
            </p>
          </div>
        ) : menuLoadError ? (
          <div className="px-5 pt-14 pb-40 font-nunito">
            <div className="bg-white border border-[#F2ECE4] rounded-[24px] p-5 text-center shadow-[0_12px_24px_-8px_rgba(46,21,19,0.06)]">
              <h2 className="text-[18px] font-extrabold text-[#2E1513] mb-2">
                Menu unavailable
              </h2>
              <p className="text-[13px] font-bold text-[#8E8075] leading-5 mb-4">
                {menuLoadError}
              </p>
              <button
                onClick={retryMenuLoad}
                className="bg-[#C27A3F] text-white px-5 py-2.5 rounded-full text-[13px] font-black shadow-sm active:scale-95 transition-transform"
              >
                Retry
              </button>
            </div>
          </div>
        ) : menuItems.length === 0 ? (
          <div className="px-5 pt-14 pb-40 font-nunito">
            <div className="bg-white border border-[#F2ECE4] rounded-[24px] p-5 text-center shadow-[0_12px_24px_-8px_rgba(46,21,19,0.06)]">
              <h2 className="text-[18px] font-extrabold text-[#2E1513] mb-2">
                No menu items
              </h2>
              <p className="text-[13px] font-bold text-[#8E8075] leading-5">
                Backend connected hai, lekin is client ke liye available menu items nahi mile.
              </p>
            </div>
          </div>
        ) : (
          /* Main Page Dynamic Render Content based on Active Tab */
          <main>
            {activeTab === 'menu' && (
              <CategoryGrid onSelectItem={handleSelectItem} menuItems={menuItems} />
            )}
            
            {activeTab === 'search' && (
              <Search onSelectItem={handleSelectItem} menuItems={menuItems} />
            )}

            {activeTab === 'cart' && (
              <CheckoutDrawer />
            )}

            {activeTab === 'status' && (
              <OrderStatus />
            )}

            {activeTab === 'report' && (
              <Report />
            )}
          </main>
        )}

        {/* Dynamic Item Customizer Sheet */}
        <ItemSheet item={selectedItem} onClose={() => setSelectedItem(null)} />

        {/* QR Code Scanner Overlay Modal */}
        <QrScannerModal
          onScanSuccess={handleQrScanSuccess}
        />

        {/* Customer Authentication & WhatsApp OTP Modal */}
        <CustomerAuthModal />

        {/* Horizontal Category Strip above Footer */}
        {activeTab === 'menu' && isCategoryOpen && (
          <CategoryStrip menuItems={menuItems} />
        )}

        {/* Sticky Floating Cart Footer Card */}
        <FloatingCartHeader />

        {/* Bottom Floating Navigation Menu */}
        <BottomNav />
      </div>
    </div>
  );
};

export default App;
