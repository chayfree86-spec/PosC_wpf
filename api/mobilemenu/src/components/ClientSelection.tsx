import React, { useEffect, useState } from 'react';
import { Loader2, QrCode } from 'lucide-react';
import { PocketBaseService } from '../services/pocketbase';
import { useCartStore } from '../store/useCartStore';

// Custom crisp SVG icons with geometricPrecision rendering to ensure maximum clarity (no blur)
const CoffeeIcon: React.FC<React.SVGProps<SVGSVGElement>> = (props) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    shapeRendering="geometricPrecision"
    {...props}
  >
    <path d="M17 8h1a4 4 0 1 1 0 8h-1" />
    <path d="M3 8h14v9a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4Z" />
    <line x1="6" x2="6" y1="2" y2="4" />
    <line x1="10" x2="10" y1="2" y2="4" />
    <line x1="14" x2="14" y1="2" y2="4" />
  </svg>
);

const CookingPotIcon: React.FC<React.SVGProps<SVGSVGElement>> = (props) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    shapeRendering="geometricPrecision"
    {...props}
  >
    <path d="M2 12h20" />
    <path d="M20 12v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-8" />
    <path d="M4 8h16a2 2 0 0 1 2 2v2H2v-2a2 2 0 0 1 2-2Z" />
    <path d="M9 3h6" />
    <path d="M12 3v5" />
  </svg>
);

const StoreIcon: React.FC<React.SVGProps<SVGSVGElement>> = (props) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    shapeRendering="geometricPrecision"
    {...props}
  >
    <path d="m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7" />
    <path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8" />
    <path d="M15 22v-4a2 2 0 0 0-2-2h-2a2 2 0 0 0-2 2v4" />
    <path d="M2 7h20" />
    <path d="M20 7v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7" />
  </svg>
);

// Per-restaurant style metadata with premium multi-stop gradients
const CLIENT_META: Record<string, { subtitle: string; Icon: React.ComponentType<any>; gradient: string }> = {
  chaychaupal: { 
    subtitle: 'Cafe', 
    Icon: CoffeeIcon, 
    gradient: 'linear-gradient(135deg, #DF8D4F 0%, #C27A3F 45%, #7A451B 100%)' 
  },
  daalroti: { 
    subtitle: 'Dhaba', 
    Icon: CookingPotIcon, 
    gradient: 'linear-gradient(135deg, #C26223 0%, #A85418 45%, #5C2E0E 100%)' 
  },
};

const metaFor = (slug: string) =>
  CLIENT_META[slug] || { 
    subtitle: 'Restaurant', 
    Icon: StoreIcon, 
    gradient: 'linear-gradient(135deg, #9C8F85 0%, #7D7067 45%, #3F362F 100%)' 
  };

export const ClientSelection: React.FC = () => {
  const setSelectedClient = useCartStore((s) => s.setSelectedClient);
  const setIsQrScannerOpen = useCartStore((s) => s.setIsQrScannerOpen);
  const autoSelectClient = useCartStore((s) => s.autoSelectClient);
  const [clients, setClients] = useState<{ id: number; name: string; slug: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [picking, setPicking] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    PocketBaseService.getClients()
      .then((list) => { if (alive) { setClients(list); setLoading(false); } })
      .catch(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);

  const pick = (c: { name: string; slug: string }) => {
    setPicking(c.slug);
    setSelectedClient({ slug: c.slug, name: c.name });
    
    // Clean client query params from URL to prevent reload loops
    try {
      const url = new URL(window.location.href);
      url.searchParams.delete('client');
      url.searchParams.delete('pos_client');
      window.history.replaceState(null, '', url.toString());
    } catch (e) {}

    // Reload so menu, branding and ordering all refetch under the chosen client.
    setTimeout(() => window.location.reload(), 120);
  };

  // Trigger automatic selection if autoSelectClient is parsed from QR URL
  useEffect(() => {
    if (autoSelectClient && clients.length > 0) {
      const matched = clients.find((c) => c.slug === autoSelectClient.slug);
      if (matched) {
        const timer = setTimeout(() => {
          pick(matched);
        }, 1200);
        return () => clearTimeout(timer);
      }
    }
  }, [autoSelectClient, clients]);

  return (
    <div className="h-screen min-h-screen bg-gradient-to-b from-[#FAF6F0] to-[#EFE3D2] font-nunito flex flex-col overflow-hidden">
      {/* Title */}
      <div className="px-5 pt-9 pb-4 text-center shrink-0">
        <h1 className="text-[26px] font-black text-[#2E1513] leading-tight tracking-tight">Where are you dining?</h1>
        <p className="text-[13px] font-semibold text-[#8E8075] mt-1.5">Pick your restaurant to start ordering</p>
      </div>

      {/* QR Code Scanner Button on landing page */}
      <div className="px-4 pb-2 shrink-0">
        <button
          type="button"
          onClick={() => setIsQrScannerOpen(true)}
          className="w-full bg-[#2E1513] hover:bg-[#421F1C] active:scale-[0.98] transition-all text-white font-black text-[13.5px] py-3.5 px-6 rounded-full flex items-center justify-center gap-2.5 shadow-md cursor-pointer border border-white/10"
        >
          <QrCode className="w-5 h-5 text-white animate-pulse" />
          <span>Scan Table QR Code</span>
        </button>
      </div>

      {loading ? (
        <div className="flex-1 flex flex-col items-center justify-center gap-3">
          <Loader2 className="w-9 h-9 text-[#C27A3F] animate-spin" />
          <p className="text-[13px] font-bold text-[#8E8075]">Loading restaurants…</p>
        </div>
      ) : clients.length === 0 ? (
        <div className="flex-1 flex flex-col items-center justify-center gap-3 text-center px-6">
          <StoreIcon className="w-12 h-12 text-[#C9BBAA]" />
          <p className="text-[14px] font-bold text-[#7D7067]">Couldn't load restaurants.</p>
          <button
            onClick={() => window.location.reload()}
            className="mt-1 bg-[#2E1513] text-white font-bold text-[13px] px-6 py-3 rounded-full active:scale-95 transition-transform"
          >
            Retry
          </button>
        </div>
      ) : (
        // Stacked vertically, taking full horizontal width.
        <div className="flex-1 flex flex-col gap-4 px-4 pb-6 min-h-0 overflow-y-auto">
          {clients.map((c) => {
            const meta = metaFor(c.slug);
            const Icon = meta.Icon;
            const isPicking = picking === c.slug;
            return (
              <button
                key={c.slug}
                type="button"
                disabled={picking !== null}
                onClick={() => pick(c)}
                className="relative flex-1 w-full rounded-[28px] overflow-hidden flex flex-col items-center justify-center p-6 transition-all duration-300 transform active:scale-[0.97] hover:scale-[1.01] border border-white/10 text-center min-h-[170px] shadow-[0_12px_36px_rgba(46,21,19,0.14)] disabled:opacity-75 cursor-pointer group"
                style={{ background: meta.gradient }}
              >
                {/* Premium Design Accents */}
                <span className="pointer-events-none absolute -top-16 -right-16 w-40 h-40 rounded-full bg-white/10 blur-3xl transition-transform duration-500 group-hover:scale-110" />
                <span className="pointer-events-none absolute -bottom-20 -left-20 w-44 h-44 rounded-full bg-black/20 blur-3xl transition-transform duration-500 group-hover:scale-110" />
                
                {/* Inset Decorative Border for a classic premium feel */}
                <span className="pointer-events-none absolute inset-2.5 border border-white/10 rounded-[20px] transition-all duration-300 group-hover:inset-3 group-hover:border-white/15" />

                {/* Center Content: Icon & Headings */}
                <div className="flex flex-col items-center justify-center z-10 transition-transform duration-300 group-hover:scale-[1.02]">
                  {/* Icon Container (Glow effect & precise inline rendering) */}
                  <div className="w-18 h-18 rounded-full bg-white/10 border border-white/20 flex items-center justify-center shrink-0 shadow-inner relative overflow-hidden backdrop-blur-sm transition-transform duration-300 group-hover:scale-105 group-hover:border-white/30 mb-4">
                    {/* Shimmer effect inside icon container */}
                    <span className="absolute inset-0 bg-gradient-to-r from-transparent via-white/5 to-transparent -translate-x-full animate-[shimmer_2s_infinite]" />
                    
                    {isPicking ? (
                      <Loader2 className="w-9 h-9 text-white animate-spin" />
                    ) : (
                      <Icon className="w-9 h-9 text-white filter drop-shadow-sm transition-transform duration-300 group-hover:rotate-6" />
                    )}
                  </div>

                  {/* Restaurant Name */}
                  <h3 className="text-[23px] font-black text-white leading-tight tracking-wide drop-shadow-[0_2px_4px_rgba(0,0,0,0.2)]">
                    {c.name}
                  </h3>

                  {/* Category Subtitle Pill */}
                  <span className="mt-2 text-[10px] font-black uppercase tracking-[0.25em] text-white bg-white/15 px-3.5 py-1.5 rounded-full border border-white/15 shadow-sm transition-colors duration-300 group-hover:bg-white/20 group-hover:border-white/25">
                    {meta.subtitle}
                  </span>
                </div>
              </button>
            );
          })}
        </div>
      )}

      <p className="text-center text-[11px] text-[#A89D8F] font-semibold pb-4 shrink-0">
        Switch restaurants anytime from the menu
      </p>
    </div>
  );
};

export default ClientSelection;
