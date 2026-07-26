import React from 'react';
import { Search as SearchIcon, Plus, EyeOff, Mic } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import type { MenuItem } from '../services/pocketbase';
import { SafeImage } from './SafeImage';

interface SearchProps {
  onSelectItem: (item: MenuItem) => void;
  menuItems: MenuItem[];
}

const normalizeSearchText = (value: string) => {
  return value.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '');
};

const aliasMap: Record<string, string[]> = {
  pb: ['paavbhaji', 'pavbhaji', 'पावभाजी', 'पाव भाजी'],
  rt: ['rt', 'regulartea', 'regularchai', 'रेगुलरटी', 'रेगुलर टी', 'चाय'],
  ccc: ['ccc', 'chocolatecoldcoffee', 'coldcoffee', 'चॉकलेटकोल्डकॉफ़ी', 'चॉकलेट कोल्ड कॉफ़ी'],
  tea: ['tea', 'chai', 'chay', 'chaay', 'चाय', 'टी'],
  coffee: ['coffee', 'कॉफ़ी', 'कॉफी'],
  icecream: ['icecream', 'ice cream', 'आइसक्रीम', 'icecream'],
  nashta: ['nashta', 'breakfast', 'snacks', 'नाश्ता', 'ब्रेकफास्ट'],
};

const expandQuery = (query: string) => {
  const normalized = normalizeSearchText(query);
  const directAliases = aliasMap[normalized] || [];
  const matchingAliases = Object.entries(aliasMap)
    .filter(([, aliases]) => aliases.some((alias) => normalizeSearchText(alias) === normalized))
    .flatMap(([, aliases]) => aliases);

  const rawTerms = directAliases.length > 0 && normalized === 'pb'
    ? directAliases
    : [query, normalized, ...directAliases, ...matchingAliases];

  return Array.from(new Set(rawTerms.map(normalizeSearchText).filter(Boolean)));
};

const itemMatchesQuery = (item: MenuItem, terms: string[]) => {
  const itemCode = normalizeSearchText(item.code);
  const searchableText = [
    item.name,
    item.description,
    item.category,
    item.subcategory || '',
  ].map(normalizeSearchText).filter(Boolean);

  return terms.some((term) => (
    (itemCode && itemCode === term) ||
    searchableText.some((field) => field.includes(term))
  ));
};

export const Search: React.FC<SearchProps> = ({ onSelectItem, menuItems }) => {
  const { searchQuery, setSearchQuery } = useCartStore();
  const [isListening, setIsListening] = React.useState(false);
  const recognitionRef = React.useRef<any>(null);

  // Initialize Speech Recognition
  React.useEffect(() => {
    const SpeechRecognition = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (SpeechRecognition) {
      const rec = new SpeechRecognition();
      rec.continuous = false;
      rec.interimResults = false;
      rec.lang = 'hi-IN'; // Listen to Hindi / Indian English perfectly!

      rec.onstart = () => {
        setIsListening(true);
      };

      rec.onresult = (event: any) => {
        const transcript = event.results[0][0].transcript;
        if (transcript) {
          // Remove trailing period if recognition adds one
          const cleanText = transcript.replace(/\.$/, '');
          setSearchQuery(cleanText);
        }
        setIsListening(false);
      };

      rec.onerror = (event: any) => {
        console.error('Speech recognition error:', event.error);
        setIsListening(false);
      };

      rec.onend = () => {
        setIsListening(false);
      };

      recognitionRef.current = rec;
    }
  }, [setSearchQuery]);

  const toggleVoiceSearch = () => {
    if (!recognitionRef.current) {
      alert('Voice Search is not supported in this browser. Please use Google Chrome or Safari.');
      return;
    }

    if (isListening) {
      recognitionRef.current.stop();
    } else {
      try {
        recognitionRef.current.start();
      } catch (err) {
        console.error('Failed to start speech recognition:', err);
      }
    }
  };

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchQuery(e.target.value);
  };

  const queryTerms = expandQuery(searchQuery);
  const normalizedQuery = normalizeSearchText(searchQuery);
  const exactCodeItems = normalizedQuery
    ? menuItems.filter((item) => normalizeSearchText(item.code) === normalizedQuery)
    : [];
  const sourceItems = exactCodeItems.length > 0
    ? exactCodeItems
    : menuItems.filter((item) => itemMatchesQuery(item, queryTerms));
  const filteredItems = sourceItems
    .sort((a, b) => b.price - a.price || a.name.localeCompare(b.name));

  return (
    <div className="px-5 pb-48 font-nunito animate-[fadeIn_0.4s_ease-out]">
      <h2 className="text-[20px] font-extrabold text-[#2E1513] border-b border-[#F4EFEA] pb-3 mb-5">
        Search Menu
      </h2>

      {/* Search Results */}
      {searchQuery ? (
        filteredItems.length > 0 ? (
          <div className="space-y-4">
            {filteredItems.map((item) => (
              <div
                key={item.id}
                onClick={() => onSelectItem(item)}
                className="group relative flex gap-4 bg-white border border-[#FAF6F0] rounded-[24px] p-3 shadow-[0_4px_16px_rgba(46,21,19,0.03)] cursor-pointer hover:border-[#E2D8CD] active:scale-[0.99] transition-all duration-200"
              >
                {/* Image */}
                <div className="w-20 h-20 rounded-[16px] overflow-hidden flex-shrink-0 bg-[#2E1513]/5">
                  <SafeImage
                    src={item.image}
                    alt={item.name}
                    className="menu-fit-image"
                    fallbackType={item.category === 'Artisan Tea' || item.category === 'Coffee' || item.category === 'Green Tea' || item.category === 'Summer' ? 'drink' : 'food'}
                  />
                </div>

                {/* Info */}
                <div className="flex flex-col justify-between py-0.5 flex-grow pr-1">
                  <div>
                    <span className="text-[9px] font-extrabold text-[#C27A3F] uppercase tracking-wider">
                      {item.category}
                    </span>
                    <h4 className="font-extrabold text-[15px] text-[#2E1513] leading-tight group-hover:text-[#C27A3F] transition-colors mt-0.5">
                      {item.name}
                    </h4>
                  </div>

                  <div className="flex items-center justify-between mt-2">
                    <span className="font-extrabold text-[15px] text-[#2E1513]">
                      ₹{item.price.toFixed(2)}
                    </span>
                    <button
                      className="bg-[#FAF6F0] text-[#2E1513] hover:bg-[#C27A3F] hover:text-white border border-[#EBE3D7] hover:border-[#C27A3F] p-1 rounded-full shadow-sm transition-all"
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectItem(item);
                      }}
                    >
                      <Plus className="w-3.5 h-3.5 stroke-[2.5]" />
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center py-12 text-center text-[#8E8075] space-y-3">
            <EyeOff className="w-10 h-10" />
            <p className="font-bold text-[14px]">No dishes matched your search</p>
          </div>
        )
      ) : (
        <div className="min-h-[48vh] pt-6">
          <div className="rounded-[24px] border border-[#EFECE6] bg-white/70 p-5 shadow-[0_10px_22px_-14px_rgba(46,21,19,0.18)]">
            <h3 className="text-[16px] font-black text-[#2E1513] mb-3">
              सर्च कैसे करें?
            </h3>
            <div className="space-y-2.5 text-[13px] font-bold text-[#7D7067] leading-5">
              <p>शॉर्ट कोड टाइप करें: <span className="text-[#2E1513]">pb = Paav Bhaji</span>, <span className="text-[#2E1513]">rt = Regular Tea</span>, <span className="text-[#2E1513]">ccc = Chocolate Cold Coffee</span>.</p>
              <p>English या Hindi में भी लिख सकते हैं: <span className="text-[#2E1513]">paav bhaji</span>, <span className="text-[#2E1513]">tea</span>, <span className="text-[#2E1513]">coffee</span>, <span className="text-[#2E1513]">icecream</span>, <span className="text-[#2E1513]">nashta</span>.</p>
            </div>
          </div>
        </div>
      )}

      {/* Sticky Bottom Search Input Field */}
      <div className="fixed bottom-[76px] left-0 right-0 max-w-[480px] mx-auto z-30 bg-[#FAF6F0]/95 backdrop-blur-md border-t border-[#F0EAE1] p-4 shadow-[0_-4px_16px_rgba(46,21,19,0.02)]">
        <div className="relative flex items-center w-full">
          <SearchIcon className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-[#8E8075]" />
          <input
            type="text"
            value={searchQuery}
            onChange={handleSearchChange}
            placeholder={isListening ? "Listening..." : "Search menu..."}
            className="w-full bg-white border border-[#EFECE6] rounded-2xl pl-12 pr-12 py-3.5 text-[15px] font-semibold text-[#2E1513] placeholder-[#8E8075] focus:border-[#C27A3F] focus:ring-2 focus:ring-[#C27A3F]/15 outline-none shadow-[0_2px_8px_-2px_rgba(46,21,19,0.03)] transition-all duration-200"
          />
          <button
            type="button"
            onClick={toggleVoiceSearch}
            className={`absolute right-3.5 p-2 rounded-xl transition-all duration-200 flex items-center justify-center ${
              isListening
                ? 'bg-red-500 text-white shadow-[0_4px_12px_rgba(239,68,68,0.3)] animate-pulse'
                : 'text-[#8E8075] hover:text-[#2E1513] hover:bg-[#FAF6F0] active:scale-90'
            }`}
            title="Voice Search"
          >
            <Mic className={`w-5 h-5 stroke-[2] ${isListening ? 'animate-bounce' : ''}`} />
          </button>
        </div>
      </div>
    </div>
  );
};
