import { create } from 'zustand';
import { PocketBaseService } from '../services/pocketbase';

export interface CartItem {
  id: string;
  name: string;
  price: number;
  image: string;
  quantity: number;
  customizations?: string[];
  specialInstructions?: string;
}

export interface OrderHistoryItem {
  id: string;
  tableNumber: string;
  items: {
    id: string;
    name: string;
    price: number;
    quantity: number;
  }[];
  total: number;
  status: 'none' | 'pending' | 'accepted' | 'preparing' | 'ready' | 'served' | 'rejected';
  created: string;
}

interface CartState {
  cart: CartItem[];
  tableNumber: string;
  tableId: string;
  activeCategory: string;
  selectedSubcategory: string;
  searchQuery: string;
  activeTab: 'menu' | 'search' | 'cart' | 'status' | 'report';
  orderStatus: 'none' | 'pending' | 'accepted' | 'preparing' | 'ready' | 'served' | 'rejected';
  currentOrderId: string | null;
  isCategoryOpen: boolean;
  isQrScannerOpen: boolean;
  orderHistory: OrderHistoryItem[];
  customer: { name?: string, mobile: string } | null;
  isAuthModalOpen: boolean;
  selectedClient: { slug: string; name: string } | null;
  autoSelectClient: { slug: string } | null;
  businessInfo: { name: string; logoUrl: string | null; downloadImages: { url: string; filename?: string }[] };
  checkoutPending: boolean;
  isOrderingLoading: boolean;
  
  // Actions
  setIsQrScannerOpen: (open: boolean) => void;
  addToCart: (item: Omit<CartItem, 'quantity'>, quantity: number, customizations?: string[], specialInstructions?: string) => void;
  removeFromCart: (id: string, customizationsKey?: string) => void;
  updateQuantity: (id: string, quantity: number, customizationsKey?: string) => void;
  clearCart: () => void;
  setTableNumber: (table: string) => void;
  setTableId: (id: string) => void;
  setActiveCategory: (category: string) => void;
  setSelectedSubcategory: (subcategory: string) => void;
  setSearchQuery: (query: string) => void;
  setActiveTab: (tab: 'menu' | 'search' | 'cart' | 'status' | 'report') => void;
  setOrderStatus: (status: CartState['orderStatus']) => void;
  setCurrentOrderId: (id: string | null) => void;
  setIsCategoryOpen: (open: boolean) => void;
  addOrderToHistory: (order: Omit<OrderHistoryItem, 'created' | 'status'>) => void;
  setCustomer: (customer: { name?: string, mobile: string } | null) => void;
  setIsAuthModalOpen: (open: boolean) => void;
  setBusinessInfo: (info: { name: string; logoUrl: string | null; downloadImages?: { url: string; filename?: string }[] }) => void;
  setSelectedClient: (client: { slug: string; name: string } | null) => void;
  setAutoSelectClient: (client: { slug: string } | null) => void;
  setCheckoutPending: (pending: boolean) => void;
  setIsOrderingLoading: (loading: boolean) => void;
  submitCurrentOrder: () => Promise<void>;
  verifyCustomerOnScan: () => Promise<boolean>;
  
  // Getters
  getCartTotal: () => number;
  getCartCount: () => number;
}

export const getCookie = (name: string): string => {
  try {
    const nameEQ = name + "=";
    const ca = document.cookie.split(';');
    for (let i = 0; i < ca.length; i++) {
      let c = ca[i];
      while (c.charAt(0) === ' ') c = c.substring(1, c.length);
      if (c.indexOf(nameEQ) === 0) return decodeURIComponent(c.substring(nameEQ.length, c.length));
    }
  } catch (e) {
    console.error('Error reading cookie:', e);
  }
  return '';
};

export const setCookie = (name: string, value: string, days: number = 365) => {
  try {
    let expires = "";
    if (days) {
      const date = new Date();
      date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
      expires = "; expires=" + date.toUTCString();
    }
    document.cookie = name + "=" + encodeURIComponent(value || "") + expires + "; path=/; SameSite=Lax";
  } catch (e) {
    console.error('Error setting cookie:', e);
  }
};

export const deleteCookie = (name: string) => {
  document.cookie = name + "=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
};

const getInitialCustomer = () => {
  try {
    const local = localStorage.getItem('elevated_customer');
    if (local) return JSON.parse(local);
  } catch (e) {}
  return null;
};

const getInitialCart = (): CartItem[] => {
  try {
    const local = localStorage.getItem('elevated_cart');
    if (local) return JSON.parse(local);
  } catch (e) {
    console.error('Error loading cart:', e);
  }
  return [];
};

const getInitialOrderStatus = (): CartState['orderStatus'] => {
  try {
    const status = localStorage.getItem('elevated_order_status') as CartState['orderStatus'];
    if (status === 'served' || status === 'rejected') {
      localStorage.removeItem('elevated_order_status');
      localStorage.removeItem('elevated_current_order_id');
      return 'none';
    }
    return status || 'none';
  } catch (e) {
    return 'none';
  }
};

const getInitialCurrentOrderId = () => {
  try {
    const status = localStorage.getItem('elevated_order_status');
    if (status === 'served' || status === 'rejected') {
      localStorage.removeItem('elevated_current_order_id');
      localStorage.removeItem('elevated_order_status');
      return null;
    }
    return localStorage.getItem('elevated_current_order_id') || null;
  } catch (e) {
    return null;
  }
};

const getInitialTableNumber = () => {
  try {
    const params = new URLSearchParams(window.location.search);
    const param = params.get('table') || params.get('t') || params.get('token') || params.get('qr_token') || params.get('qr_code') || '';
    const isToken = /^[a-fA-F0-9]{32}$/.test(param);
    if (param && !isToken) return param;

    return sessionStorage.getItem('elevated_table_number') || localStorage.getItem('elevated_table_number') || '';
  } catch (e) {}
  return '';
};

const getInitialTableId = () => {
  try {
    const params = new URLSearchParams(window.location.search);
    const param = params.get('table') || params.get('t') || params.get('token') || params.get('qr_token') || params.get('qr_code') || '';
    const isToken = /^[a-fA-F0-9]{32}$/.test(param);
    if (param && isToken) return param;

    return sessionStorage.getItem('elevated_table_id') || localStorage.getItem('elevated_table_id') || '';
  } catch (e) {}
  return '';
};

const getInitialSelectedClient = (): { slug: string; name: string } | null => {
  try {
    const params = new URLSearchParams(window.location.search);
    const clientParam = params.get('client') || params.get('pos_client');
    // If client is in the URL, return null so we show the Client Selection page first
    if (clientParam) {
      return null;
    }

    const slug = localStorage.getItem('pos_selected_client') || '';
    const name = localStorage.getItem('pos_selected_client_name') || '';
    return slug ? { slug, name } : null;
  } catch (e) {}
  return null;
};

// Only the slug is read from the URL. The display name comes from /auth/clients when the
// list loads -- guessing it from the slug here meant a restaurant renamed on the server kept
// showing its old name on the landing screen.
const getInitialAutoSelectClient = (): { slug: string } | null => {
  try {
    const params = new URLSearchParams(window.location.search);
    const clientParam = params.get('client') || params.get('pos_client');
    if (clientParam) {
      return { slug: clientParam.toLowerCase().replace(/[^a-z0-9_-]/g, '') };
    }
  } catch (e) {}
  return null;
};

// Custom helper to generate unique key for items with different customizations
const getCartItemKey = (id: string, customizations?: string[]) => {
  if (!customizations || customizations.length === 0) return id;
  return `${id}-${[...customizations].sort().join(',')}`;
};

export const useCartStore = create<CartState>((set, get) => ({
  cart: getInitialCart(),
  tableNumber: getInitialTableNumber(),
  tableId: getInitialTableId(),
  // No category is active until the menu arrives and the first one is picked from it --
  // a hardcoded 'Breakfast' highlighted a category most clients don't even have.
  activeCategory: '',
  selectedSubcategory: 'All',
  searchQuery: '',
  activeTab: 'menu',
  orderStatus: getInitialOrderStatus(),
  currentOrderId: getInitialCurrentOrderId(),
  isCategoryOpen: false,
  isQrScannerOpen: false,
  orderHistory: JSON.parse(localStorage.getItem('elevated_order_history') || '[]'),
  customer: getInitialCustomer(),
  isAuthModalOpen: false,
  selectedClient: getInitialSelectedClient(),
  autoSelectClient: getInitialAutoSelectClient(),
  // Blank until /settings answers. Seeding a brand name here showed it on every client's
  // header for the first paint, including the ones it doesn't belong to.
  businessInfo: { name: '', logoUrl: null, downloadImages: [] },
  checkoutPending: false,
  isOrderingLoading: false,

  setIsQrScannerOpen: (isQrScannerOpen) => set({ isQrScannerOpen }),
  addToCart: (item, quantity, customizations = [], specialInstructions = '') => {
    const customer = get().customer;
    if (!customer) {
      set({ isAuthModalOpen: true });
      return;
    }
    const cart = get().cart;
    const itemKey = getCartItemKey(item.id, customizations);
    
    const existingItemIndex = cart.findIndex(
      (cartItem) => getCartItemKey(cartItem.id, cartItem.customizations) === itemKey
    );

    let updatedCart;
    if (existingItemIndex > -1) {
      updatedCart = [...cart];
      updatedCart[existingItemIndex].quantity += quantity;
      if (specialInstructions) {
        updatedCart[existingItemIndex].specialInstructions = specialInstructions;
      }
    } else {
      updatedCart = [...cart, { ...item, quantity, customizations, specialInstructions }];
    }
    set({ cart: updatedCart });
    try {
      localStorage.setItem('elevated_cart', JSON.stringify(updatedCart));
    } catch (e) {}
  },

  removeFromCart: (id, customizationsKey) => {
    const cart = get().cart;
    const updatedCart = cart.filter((cartItem) => {
      const itemKey = getCartItemKey(cartItem.id, cartItem.customizations);
      const targetKey = customizationsKey || id;
      return itemKey !== targetKey;
    });
    set({ cart: updatedCart });
    try {
      localStorage.setItem('elevated_cart', JSON.stringify(updatedCart));
    } catch (e) {}
  },

  updateQuantity: (id, quantity, customizationsKey) => {
    if (quantity <= 0) {
      get().removeFromCart(id, customizationsKey);
      return;
    }
    const cart = get().cart;
    const updatedCart = cart.map((cartItem) => {
      const itemKey = getCartItemKey(cartItem.id, cartItem.customizations);
      const targetKey = customizationsKey || id;
      if (itemKey === targetKey) {
        return { ...cartItem, quantity };
      }
      return cartItem;
    });
    set({ cart: updatedCart });
    try {
      localStorage.setItem('elevated_cart', JSON.stringify(updatedCart));
    } catch (e) {}
  },

  clearCart: () => {
    set({ cart: [] });
    try {
      localStorage.removeItem('elevated_cart');
    } catch (e) {}
  },
  setTableNumber: (tableNumber) => {
    set({ tableNumber });
    try {
      if (tableNumber) {
        localStorage.setItem('elevated_table_number', tableNumber);
        sessionStorage.setItem('elevated_table_number', tableNumber);
      } else {
        localStorage.removeItem('elevated_table_number');
        sessionStorage.removeItem('elevated_table_number');
      }
    } catch (e) {}
  },
  setTableId: (tableId) => {
    set({ tableId });
    try {
      if (tableId) {
        localStorage.setItem('elevated_table_id', tableId);
        sessionStorage.setItem('elevated_table_id', tableId);
      } else {
        localStorage.removeItem('elevated_table_id');
        sessionStorage.removeItem('elevated_table_id');
      }
    } catch (e) {}
  },
  setActiveCategory: (activeCategory) => set({ activeCategory, selectedSubcategory: 'All' }),
  setSelectedSubcategory: (selectedSubcategory) => set({ selectedSubcategory }),
  setSearchQuery: (searchQuery) => set({ searchQuery }),
  setActiveTab: (activeTab) => set({ activeTab, isCategoryOpen: false }),
  setOrderStatus: (orderStatus) => {
    set({ orderStatus });
    try {
      if (orderStatus) {
        localStorage.setItem('elevated_order_status', orderStatus);
      } else {
        localStorage.removeItem('elevated_order_status');
      }
    } catch (e) {}
    
    // Dynamic sync inside order history
    const currentOrderId = get().currentOrderId;
    if (currentOrderId) {
      const updatedHistory = get().orderHistory.map((o) =>
        o.id === currentOrderId ? { ...o, status: orderStatus } : o
      );
      set({ orderHistory: updatedHistory });
      localStorage.setItem('elevated_order_history', JSON.stringify(updatedHistory));
    }
  },
  setCurrentOrderId: (currentOrderId) => {
    set({ currentOrderId });
    try {
      if (currentOrderId) {
        localStorage.setItem('elevated_current_order_id', currentOrderId);
      } else {
        localStorage.removeItem('elevated_current_order_id');
        localStorage.removeItem('elevated_order_status');
      }
    } catch (e) {}
  },
  setIsCategoryOpen: (isCategoryOpen) => set({ isCategoryOpen }),
  
  addOrderToHistory: (order) => {
    const newOrder: OrderHistoryItem = {
      ...order,
      status: 'pending',
      created: new Date().toISOString()
    };
    const updatedHistory = [newOrder, ...get().orderHistory];
    set({ orderHistory: updatedHistory });
    localStorage.setItem('elevated_order_history', JSON.stringify(updatedHistory));
  },

  setCustomer: (customer) => {
    set({ customer });
    if (customer) {
      setCookie('pos_customer_mobile', customer.mobile, 365);
      if (customer.name) {
        setCookie('pos_customer_name', customer.name, 365);
      } else {
        deleteCookie('pos_customer_name');
      }
      localStorage.setItem('elevated_customer', JSON.stringify(customer));
    } else {
      deleteCookie('pos_customer_mobile');
      deleteCookie('pos_customer_name');
      localStorage.removeItem('elevated_customer');
    }
  },

  verifyCustomerOnScan: async () => {
    try {
      const local = localStorage.getItem('elevated_customer');
      if (!local) {
        set({ customer: null, isAuthModalOpen: true });
        return false;
      }
      const savedCustomer = JSON.parse(local);
      if (!savedCustomer || !savedCustomer.mobile) {
        set({ customer: null, isAuthModalOpen: true });
        return false;
      }

      const checkRes = await PocketBaseService.checkCustomer(savedCustomer.mobile);
      if (checkRes.exists && checkRes.customer) {
        set({
          customer: {
            name: checkRes.customer.name || savedCustomer.name || '',
            mobile: savedCustomer.mobile
          }
        });
        return true;
      } else {
        set({ customer: null, isAuthModalOpen: true });
        return false;
      }
    } catch (err) {
      console.error('Failed to verify customer on scan:', err);
      set({ customer: null, isAuthModalOpen: true });
      return false;
    }
  },

  setIsAuthModalOpen: (isAuthModalOpen) => set({ isAuthModalOpen }),
  setBusinessInfo: (businessInfo) => set((state) => ({
    businessInfo: {
      ...state.businessInfo,
      ...businessInfo,
      downloadImages: businessInfo.downloadImages || [],
    },
  })),
  setSelectedClient: (selectedClient) => {
    try {
      if (selectedClient) {
        localStorage.setItem('pos_selected_client', selectedClient.slug);
        localStorage.setItem('pos_selected_client_name', selectedClient.name);
      } else {
        localStorage.removeItem('pos_selected_client');
        localStorage.removeItem('pos_selected_client_name');
      }
    } catch (e) { /* ignore */ }
    set({ selectedClient });
  },
  setAutoSelectClient: (autoSelectClient) => set({ autoSelectClient }),
  setCheckoutPending: (checkoutPending) => set({ checkoutPending }),
  setIsOrderingLoading: (isOrderingLoading) => set({ isOrderingLoading }),
  submitCurrentOrder: async () => {
    const { cart, tableNumber, tableId, getCartTotal, addOrderToHistory, setCurrentOrderId, setOrderStatus, clearCart, setActiveTab } = get();
    if ((!tableNumber && !tableId) || cart.length === 0) return;
    
    set({ isOrderingLoading: true });
    try {
      const subtotal = getCartTotal();
      const grandTotal = subtotal;
      
      const orderItems = cart.map((item) => ({
        id: item.id,
        name: item.name,
        price: item.price,
        quantity: item.quantity,
        customizations: item.customizations,
        specialInstructions: item.specialInstructions
      }));

      // Submit order to the POS API (QR-order queue for the operator).
      const submittedOrder = await PocketBaseService.submitOrder(
        tableId,
        tableNumber,
        orderItems,
        grandTotal,
        get().customer
      );

      // Add order details to persistent order history
      addOrderToHistory({
        id: submittedOrder.id,
        tableNumber,
        items: cart.map((item) => ({
          id: item.id,
          name: item.name,
          price: item.price,
          quantity: item.quantity
        })),
        total: grandTotal
      });

      // Save order in state
      setCurrentOrderId(submittedOrder.id);
      setOrderStatus('pending');
      clearCart();
      setActiveTab('status'); // Switch to status tracking screen
    } catch (err) {
      console.error('Error placing order:', err);
      throw err;
    } finally {
      set({ isOrderingLoading: false });
    }
  },

  getCartTotal: () => {
    return get().cart.reduce((total, item) => total + item.price * item.quantity, 0);
  },

  getCartCount: () => {
    return get().cart.reduce((count, item) => count + item.quantity, 0);
  }
}));
