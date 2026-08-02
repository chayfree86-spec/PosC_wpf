import PocketBase from 'pocketbase';

export interface MenuItem {
  id: string;
  name: string;
  code: string;
  description: string;
  price: number;
  image: string;
  category: string;
  categoryImage: string;
  tagline: string;
  subcategory?: string;
  subcategoryImage?: string;
  customizationOptions?: {
    title: string;
    options: string[];
    multiSelect?: boolean;
  }[];
}

export interface Order {
  id: string;
  tableNumber: string;
  items: {
    id: string;
    name: string;
    price: number;
    quantity: number;
    customizations?: string[];
    specialInstructions?: string;
  }[];
  total: number;
  status: 'pending' | 'accepted' | 'preparing' | 'ready' | 'served' | 'rejected';
  created: string;
}

interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data: T;
}

type BackendMenuItem = {
  id?: string | number;
  name?: string;
  description?: string | null;
  price?: string | number | null;
  image?: string | string[] | null;
  category_name?: string | null;
  category_image?: string | null;
  category?: string | null;
  sub_category_name?: string | null;
  sub_category_image?: string | null;
  subcategory?: string | null;
  is_available?: string | number | boolean | null;
};

const pbUrl = import.meta.env.VITE_POCKETBASE_URL || 'http://127.0.0.1:8090';
export const pb = new PocketBase(pbUrl);

const normalizeBase = (value: string) => value.replace(/\/+$/, '');

const getAppRootPath = () => {
  const pathname = window.location.pathname;
  const mobileMenuIndex = pathname.toLowerCase().indexOf('/api/mobilemenu');

  if (mobileMenuIndex >= 0) {
    return pathname.slice(0, mobileMenuIndex).replace(/\/+$/, '');
  }

  const apiMenuIndex = pathname.toLowerCase().indexOf('/api/menu');
  if (apiMenuIndex >= 0) {
    return pathname.slice(0, apiMenuIndex).replace(/\/+$/, '');
  }

  return '';
};

const unique = <T,>(values: T[]) => Array.from(new Set(values.filter(Boolean)));

const getApiBaseCandidates = () => {
  const params = new URLSearchParams(window.location.search);
  const configured = import.meta.env.VITE_POS_API_URL || '';
  const appRoot = getAppRootPath();
  
  const isLocal = /^(localhost|127\.0\.0\.1|192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)/i.test(window.location.hostname) || 
                  window.location.hostname.endsWith('.local') || 
                  window.location.hostname.endsWith('.test');

  const savedBase = localStorage.getItem('pos_api_base') || '';
  const isSavedBaseLocal = /localhost|127\.0\.0\.1|192\.168\./i.test(savedBase);

  // Split candidates based on environment to prioritize the correct backend
  const localCandidates = [
    `${window.location.origin}${appRoot}/api`,
    `${window.location.origin}/api`,
    'http://localhost/possoftware-final/api',
    'http://127.0.0.1/possoftware-final/api',
  ];

  const remoteCandidates = [
    'https://posapi.chaychaupal.com/api',
    'https://report.chaychaupal.com/api',
  ];

  const list: string[] = [
    params.get('api_url') || '',
    params.get('api') || '',
  ];

  if (isLocal) {
    // We are running locally: prioritize local API endpoints
    if (savedBase && isSavedBaseLocal) {
      list.push(savedBase);
    }
    list.push(...localCandidates);
    list.push(configured);
    if (savedBase && !isSavedBaseLocal) {
      list.push(savedBase);
    }
    list.push(...remoteCandidates);
  } else {
    // We are running in production: prioritize remote API endpoints
    if (savedBase && !isSavedBaseLocal) {
      list.push(savedBase);
    }
    list.push(...remoteCandidates);
    list.push(configured);
    if (savedBase && isSavedBaseLocal) {
      list.push(savedBase);
    }
    list.push(...localCandidates);
  }

  const candidates = unique(list);

  if (/friendpos\.com$/i.test(window.location.hostname)) {
    candidates.unshift(`${window.location.origin}/index.php?__api=1`);
  }

  return unique(candidates.map(normalizeBase));
};

const getSavedAdminSession = () => {
  try {
    return JSON.parse(localStorage.getItem('pos_menu_admin_session') || '{}') as {
      token?: string;
      client?: string;
    };
  } catch (_) {
    return {};
  }
};

const getPosApiHeaders = () => {
  const params = new URLSearchParams(window.location.search);
  const savedSession = getSavedAdminSession();
  // Customer-picked restaurant (client) drives the whole PWA session (menu +
  // orders) via the X-POS-Client header. A URL ?client= still wins (admin/iframe).
  let selectedClient = '';
  try { selectedClient = localStorage.getItem('pos_selected_client') || ''; } catch (_) { /* ignore */ }
  const client = params.get('client') || params.get('pos_client') || selectedClient || savedSession.client || '';
  const token = savedSession.token || '';
  const headers: Record<string, string> = { Accept: 'application/json' };

  if (client) {
    headers['X-POS-Client'] = client;
  }
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  return headers;
};

const resolveApiUrl = (baseUrl: string, path: string) => {
  const [pathOnly, searchPart] = path.split('?');
  const normalizedPath = pathOnly.startsWith('/') ? pathOnly : `/${pathOnly}`;
  const url = new URL(baseUrl, window.location.href);

  if (url.searchParams.has('__api')) {
    const fullPath = searchPart ? `${normalizedPath}?${searchPart}` : normalizedPath;
    url.searchParams.set('__path', `/api${fullPath}`);
    return url.toString();
  }

  url.pathname = `${url.pathname.replace(/\/+$/, '')}${normalizedPath}`.replace(/\/{2,}/g, '/');
  if (searchPart) {
    const newParams = new URLSearchParams(searchPart);
    newParams.forEach((value, key) => {
      url.searchParams.set(key, value);
    });
  }
  return url.toString();
};

const getFirstImageUrl = (value: unknown): string => {
  if (Array.isArray(value)) {
    return String(value.find((item) => typeof item === 'string' && item.trim()) || '');
  }

  if (typeof value !== 'string') {
    return '';
  }

  const trimmed = value.trim();
  if (!trimmed) {
    return '';
  }

  try {
    const parsed = JSON.parse(trimmed);
    if (Array.isArray(parsed)) {
      return String(parsed.find((item) => typeof item === 'string' && item.trim()) || '');
    }
    if (typeof parsed === 'string') {
      return parsed;
    }
  } catch (_) {
    return trimmed;
  }

  return trimmed;
};

const cleanImageUrl = (url: string) => {
  const driveMatch = url.match(/(?:drive\.google\.com\/(?:uc\?(?:export=view&)?id=|open\?id=|file\/d\/))([a-zA-Z0-9_-]{25,})/);
  if (driveMatch?.[1]) {
    return `https://lh3.googleusercontent.com/d/${driveMatch[1]}`;
  }
  return url;
};

const resolveAssetUrl = (url: unknown) => {
  const rawUrl = getFirstImageUrl(url);
  if (!rawUrl) return '';

  // A Google Drive link is served straight from Drive's own CDN. This used to be proxied
  // through api/menu/gdrive-handler.php, which no longer exists — that folder was renamed to
  // api/report and the handler dropped — so the proxy URL 404'd and the image never loaded.
  const driveMatch = rawUrl.match(/(?:drive\.google\.com\/(?:uc\?(?:export=view&)?id=|open\?id=|file\/d\/)|lh3\.googleusercontent\.com\/d\/)([a-zA-Z0-9_-]{25,})/);
  if (driveMatch?.[1]) {
    return `https://lh3.googleusercontent.com/d/${driveMatch[1]}`;
  }

  const value = cleanImageUrl(rawUrl);
  if (!value || /^https?:\/\//i.test(value) || value.startsWith('data:') || value.startsWith('blob:')) {
    return value;
  }
  if (value.startsWith('/')) {
    return `${window.location.origin}${value}`;
  }
  return `${window.location.origin}${getAppRootPath()}/${value.replace(/^\/+/, '')}`;
};

const parseDescription = (raw: string | null | undefined) => {
  const fallback = {
    text: '',
    tagline: 'SELECTIONS',
    customizationOptions: [] as MenuItem['customizationOptions'],
    code: '',
  };

  if (!raw) {
    return fallback;
  }

  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      const source = parsed as Record<string, unknown>;
      const options = Array.isArray(source.customizationOptions)
        ? source.customizationOptions
        : Array.isArray(source.customizations)
          ? source.customizations
          : [];

      return {
        text: String(source.text ?? source.description ?? ''),
        tagline: String(source.tagline ?? fallback.tagline),
        customizationOptions: options as MenuItem['customizationOptions'],
        code: String(source.code ?? ''),
      };
    }
  } catch (_) {
    return { ...fallback, text: raw };
  }

  return { ...fallback, text: raw };
};

const isTruthyAvailability = (value: BackendMenuItem['is_available']) => {
  if (value === null || value === undefined) {
    return true;
  }
  return value === true || value === 1 || value === '1' || String(value).toLowerCase() === 'true';
};

const mapMenuItem = (item: BackendMenuItem): MenuItem => {
  const parsedDescription = parseDescription(item.description);

  return {
    id: String(item.id ?? ''),
    name: String(item.name ?? ''),
    code: parsedDescription.code,
    description: parsedDescription.text,
    price: Number(item.price ?? 0),
    image: resolveAssetUrl(item.image),
    category: String(item.category_name ?? item.category ?? 'Menu'),
    categoryImage: resolveAssetUrl(String(item.category_image ?? '')),
    tagline: parsedDescription.tagline,
    subcategory: String(item.sub_category_name ?? item.subcategory ?? ''),
    subcategoryImage: resolveAssetUrl(String(item.sub_category_image ?? '')),
    customizationOptions: parsedDescription.customizationOptions,
  };
};

const fetchApi = async <T,>(path: string, init?: RequestInit): Promise<T> => {
  let lastError: unknown = null;

  for (const baseUrl of getApiBaseCandidates()) {
    try {
      const mergedHeaders = Object.assign({}, getPosApiHeaders(), init?.headers);
      const response = await fetch(resolveApiUrl(baseUrl, path), Object.assign({
        credentials: 'same-origin',
        cache: 'no-store',
      }, init, {
        headers: mergedHeaders
      }));

      if (!response.ok) {
        throw new Error(`POS API request failed: ${response.status}`);
      }

      const payload = await response.json() as ApiResponse<T>;
      if (payload && typeof payload === 'object' && 'success' in payload && payload.success === false) {
        throw new Error(payload.message || 'POS API request failed');
      }

      localStorage.setItem('pos_api_base', baseUrl);
      return 'data' in payload ? payload.data : (payload as T);
    } catch (err) {
      lastError = err;
    }
  }

  throw lastError instanceof Error ? lastError : new Error('POS API request failed');
};

export const PocketBaseService = {
  async getBusinessInfo(): Promise<{ name: string; logoUrl: string | null; downloadImages: { url: string; filename?: string }[] }> {
    try {
      const settings = await fetchApi<{ key: string; value: unknown }[]>('/settings');
      const businessName = settings.find((setting) => setting.key === 'business_name')?.value;
      const businessLogo = settings.find((setting) => setting.key === 'business_logo')?.value;
      const downloadImages = settings.find((setting) => setting.key === 'mobile_menu_download_images')?.value;
      const normalizedDownloadImages = Array.isArray(downloadImages)
        ? downloadImages
            .filter((image): image is { url: string; filename?: string } => {
              return typeof image === 'object' && image !== null && typeof (image as { url?: unknown }).url === 'string';
            })
            .map((image) => ({
              url: resolveAssetUrl(image.url),
              filename: typeof image.filename === 'string' ? image.filename : undefined,
            }))
        : [];

      return {
        name: typeof businessName === 'string' && businessName.trim() ? businessName : '',
        logoUrl: typeof businessLogo === 'string' && businessLogo.trim() ? resolveAssetUrl(businessLogo) : null,
        downloadImages: normalizedDownloadImages,
      };
    } catch (err) {
      // Nothing is invented when /settings is unreachable. The header falls back to the
      // restaurant the customer actually picked, which is real data; a canned brand name
      // here used to show one restaurant's identity while ordering from another's menu.
      console.log('Business settings fetch failed.', err);
      return { name: '', logoUrl: null, downloadImages: [] };
    }
  },

  // Active restaurants (clients) the customer can pick from on the landing screen.
  async getClients(): Promise<{ id: number; name: string; slug: string }[]> {
    try {
      const list = await fetchApi<any[]>('/auth/clients');
      return Array.isArray(list) ? list.map((c) => ({ id: Number(c.id), name: String(c.name || ''), slug: String(c.slug || '') })) : [];
    } catch (_) {
      return [];
    }
  },

  async getMenu(): Promise<MenuItem[]> {
    const items = await fetchApi<BackendMenuItem[]>('/menu-items');

    return items
      .filter((item) => item.id !== undefined && item.name && isTruthyAvailability(item.is_available))
      .map(mapMenuItem);
  },

  async checkCustomer(mobile: string): Promise<{ exists: boolean; customer?: { name?: string; mobile: string } }> {
    return fetchApi<{ exists: boolean; customer?: { name?: string; mobile: string } }>(`/customers/check?mobile=${encodeURIComponent(mobile)}`);
  },

  async sendOtp(mobile: string, name: string): Promise<{ success: boolean; message: string; debug_otp?: string }> {
    return fetchApi<{ success: boolean; message: string; debug_otp?: string }>(`/customers/send-otp`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mobile, name })
    });
  },

  async verifyOtp(mobile: string, name: string, otp: string): Promise<{ success: boolean; customer?: { name?: string; mobile: string }; message?: string }> {
    return fetchApi<{ success: boolean; customer?: { name?: string; mobile: string }; message?: string }>(`/customers/verify-otp`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mobile, name, otp })
    });
  },

  async validateQr(token: string): Promise<{ valid: boolean; table?: { id: number; table_number: string } }> {
    try {
      return await fetchApi<{ valid: boolean; table?: { id: number; table_number: string } }>(`/tables/validate-qr?token=${encodeURIComponent(token)}`);
    } catch (err) {
      if (err instanceof Error && err.message.includes('404')) {
        return { valid: false };
      }
      throw err;
    }
  },

  // Place a QR order. Goes to the POS API (/qr-orders) -- the real bridge to the
  // restaurant's POS operator -- NOT PocketBase (which isn't reachable in prod).
  async submitOrder(
    tableToken: string,
    tableNumber: string,
    items: any[],
    total: number,
    customer?: { name?: string; mobile: string } | null
  ): Promise<Order> {
    const data = await fetchApi<{ id: number; status: string }>(`/qr-orders`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        table_token: tableToken || '',
        table_number: tableNumber || '',
        items,
        total,
        customer_name: customer?.name || '',
        customer_mobile: customer?.mobile || '',
      }),
    });

    return {
      id: String(data.id),
      tableNumber,
      items,
      total,
      status: (data.status as Order['status']) || 'pending',
      created: new Date().toISOString(),
    };
  },

  // Track a QR order's status by POLLING the POS API every few seconds (no
  // PocketBase realtime). Stops once the order reaches a terminal state
  // (accepted / rejected / served).
  subscribeToOrderUpdates(orderId: string, onUpdate: (order: Order) => void): () => void {
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const poll = async () => {
      if (stopped) return;
      try {
        const rec = await fetchApi<any>(`/qr-orders/${orderId}`);
        // Backend 'settled' (bill paid) -> customer-facing 'served' (completed).
        const mappedStatus = rec.status === 'settled' ? 'served' : rec.status;
        const order: Order = {
          id: String(rec.id),
          tableNumber: String(rec.table_number || ''),
          items: Array.isArray(rec.items) ? rec.items : [],
          total: Number(rec.total_amount || 0),
          status: mappedStatus,
          created: rec.created_at,
        };
        onUpdate(order);
        // Keep polling through 'accepted' so the order stays visible WITH its
        // status until the bill is settled. Stop only at a real terminal state.
        if (['rejected', 'served'].includes(mappedStatus)) {
          return; // terminal -> stop polling
        }
      } catch (_) {
        // transient error -> keep polling
      }
      if (!stopped) {
        timer = setTimeout(poll, 4000);
      }
    };

    poll();

    return () => {
      stopped = true;
      if (timer) clearTimeout(timer);
    };
  },
};
