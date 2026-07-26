import React, { useState, useRef, useEffect } from 'react';
import { ChevronDown, Check } from 'lucide-react';

interface CustomDropdownProps {
  label?: string;
  value: string;
  onChange: (value: string) => void;
  options: string[];
  placeholder?: string;
}

export const CustomDropdown: React.FC<CustomDropdownProps> = ({
  label,
  value,
  onChange,
  options,
  placeholder = 'Select an option'
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleSelect = (option: string) => {
    onChange(option);
    setIsOpen(false);
  };

  return (
    <div className="flex flex-col gap-1.5 w-full relative font-nunito" ref={containerRef}>
      {label && (
        <span className="text-[12px] font-extrabold uppercase tracking-widest text-[#7D7067] px-1">
          {label}
        </span>
      )}
      
      {/* Dropdown Header Trigger */}
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className={`flex items-center justify-between w-full bg-white border px-4 py-3 rounded-2xl transition-all duration-200 outline-none text-[15px] font-bold ${
          isOpen
            ? 'border-[#C27A3F] shadow-[0_0_0_2.5px_rgba(194,122,63,0.15)]'
            : 'border-[#EFECE6] hover:border-[#E2D8CD] shadow-[0_2px_8px_-2px_rgba(46,21,19,0.03)]'
        } ${value ? 'text-[#2E1513]' : 'text-[#8E8075]'}`}
      >
        <span>{value ? value : placeholder}</span>
        <ChevronDown
          className={`w-4 h-4 text-[#7D7067] transition-transform duration-300 ${
            isOpen ? 'rotate-180 text-[#C27A3F]' : ''
          }`}
        />
      </button>

      {/* Dropdown Options List */}
      {isOpen && (
        <div className="absolute top-[calc(100%+6px)] left-0 right-0 z-50 bg-[#FAF6F0] border border-[#EBE3D7] rounded-2xl shadow-[0_12px_32px_rgba(46,21,19,0.12)] overflow-hidden max-h-48 overflow-y-auto animate-[fadeIn_0.15s_ease-out]">
          <div className="p-1">
            {options.map((option) => {
              const isSelected = value === option;
              return (
                <button
                  key={option}
                  type="button"
                  onClick={() => handleSelect(option)}
                  className={`flex items-center justify-between w-full px-4 py-2.5 rounded-xl text-left text-[14px] font-bold transition-all duration-150 mb-0.5 last:mb-0 ${
                    isSelected
                      ? 'bg-[#2E1513] text-white'
                      : 'text-[#2E1513] hover:bg-[#EFECE6]'
                  }`}
                >
                  <span>{option}</span>
                  {isSelected && <Check className="w-4 h-4 text-white" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};
