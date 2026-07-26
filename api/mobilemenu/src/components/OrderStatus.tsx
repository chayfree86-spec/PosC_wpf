import React, { useEffect, useState } from 'react';
import { CheckCircle2, Clock, ChefHat, HeartHandshake, ArrowRight, ClipboardList, XCircle } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { PocketBaseService, type Order } from '../services/pocketbase';

export const OrderStatus: React.FC = () => {
  const { currentOrderId, orderStatus, setOrderStatus, tableNumber, setActiveTab, setTableNumber, setTableId } = useCartStore();
  const [orderDetail, setOrderDetail] = useState<Order | null>(null);

  useEffect(() => {
    if (!currentOrderId) return;

    // Load initial order details if offline/localStorage exists
    const localOrderString = localStorage.getItem(`mock-order-${currentOrderId}`);
    if (localOrderString) {
      setOrderDetail(JSON.parse(localOrderString));
    }

    // Subscribe to real-time updates from PocketBase / Simulation
    const unsubscribe = PocketBaseService.subscribeToOrderUpdates(currentOrderId, (updatedOrder) => {
      setOrderDetail(updatedOrder);
      setOrderStatus(updatedOrder.status);
      // Bill settled on the POS (status 'served') -> free the table on the PWA so
      // the session ends; the next order needs a fresh table scan.
      if (updatedOrder.status === 'served' || updatedOrder.status === 'rejected') {
        setTableNumber('');
        setTableId('');
      }
    });

    return () => {
      unsubscribe();
    };
  }, [currentOrderId, setOrderStatus]);

  // Notify the customer the instant the POS accepts or rejects the order.
  useEffect(() => {
    if (orderStatus === 'accepted' || orderStatus === 'rejected') {
      try {
        navigator.vibrate?.(orderStatus === 'accepted' ? [80, 40, 80] : [200]);
      } catch (_) { /* vibration not supported */ }
    }
  }, [orderStatus]);

  if (!currentOrderId) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] px-6 text-center font-nunito space-y-4">
        <ClipboardList className="w-12 h-12 text-[#8E8075]" />
        <h3 className="text-[19px] font-extrabold text-[#2E1513]">No Active Order</h3>
        <p className="text-[13.5px] text-[#7D7067] font-medium max-w-[240px]">
          You don't have any ongoing orders at the moment.
        </p>
        <button
          onClick={() => setActiveTab('menu')}
          className="bg-[#2E1513] text-white font-bold text-[14px] px-6 py-3 rounded-full shadow-md active:scale-95 transition-transform"
        >
          Browse Menu
        </button>
      </div>
    );
  }

  // Order was rejected by the POS operator -- show a clear rejected screen.
  if (orderStatus === 'rejected') {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] px-6 text-center font-nunito space-y-4 animate-[fadeIn_0.4s_ease-out]">
        <div className="w-16 h-16 bg-red-50 text-red-500 border border-red-100 rounded-full flex items-center justify-center">
          <XCircle className="w-9 h-9" />
        </div>
        <h3 className="text-[20px] font-extrabold text-[#2E1513]">Order Rejected</h3>
        <p className="text-[13.5px] text-[#7D7067] font-medium max-w-[260px]">
          Sorry, the restaurant could not accept this order right now. Please contact the staff or try ordering again.
        </p>
        <button
          onClick={() => setActiveTab('menu')}
          className="bg-[#2E1513] text-white font-bold text-[14px] px-6 py-3 rounded-full shadow-md active:scale-95 transition-transform"
        >
          Order Again
        </button>
      </div>
    );
  }

  // Define steps for the POS status tracker (only 3 steps: Order Sent, Accepted, Served)
  const steps = [
    { key: 'pending', label: 'Order Sent', desc: 'Sent to POS system', icon: Clock },
    { key: 'accepted', label: 'Accepted', desc: 'POS confirmed order', icon: CheckCircle2 },
    { key: 'served', label: 'Served', desc: 'Served & Enjoy!', icon: HeartHandshake }
  ];

  // Map preparing and ready to accepted so they highlight the Accepted step
  let displayStatus = orderStatus;
  if (displayStatus === 'preparing' || displayStatus === 'ready') {
    displayStatus = 'accepted';
  }

  // Get index of the current active step
  const currentStepIndex = steps.findIndex((step) => step.key === displayStatus);

  return (
    <div className="px-5 pb-32 font-nunito animate-[fadeIn_0.5s_ease-out]">
      {/* Top Banner Success */}
      <div className="bg-[#white] border border-[#F4EFEA] rounded-3xl p-6 text-center shadow-[0_8px_30px_rgba(46,21,19,0.04)] mb-6 relative overflow-hidden">
        <div className="absolute top-0 left-0 right-0 h-1.5 bg-[#C27A3F]" />
        
        {/* Animated Check */}
        <div className="w-14 h-14 bg-[#FAF6F0] text-[#C27A3F] border border-[#EBE3D7] rounded-full flex items-center justify-center mx-auto mb-4 shadow-sm">
          <ChefHat className="w-7 h-7 animate-[bounce_2s_infinite]" />
        </div>
        
        <h2 className="text-[20px] font-extrabold text-[#2E1513] mb-1.5">
          {displayStatus === 'served' ? 'Order Served!' : displayStatus === 'accepted' ? 'Order Accepted!' : 'Order Sent to POS'}
        </h2>
        <p className="text-[13px] font-semibold text-[#8D5524] uppercase tracking-wider">
          Table {orderDetail?.tableNumber || tableNumber} • Order #{currentOrderId}
        </p>
      </div>

      {/* Vertical Real-time POS Tracking Steps */}
      <div className="bg-white border border-[#F4EFEA] rounded-3xl p-6 shadow-[0_8px_30px_rgba(46,21,19,0.03)] space-y-6 mb-6">
        <h3 className="text-[14px] font-extrabold text-[#2E1513] tracking-widest uppercase border-b border-[#F4EFEA] pb-3 mb-4">
          Live POS Status Tracker
        </h3>

        <div className="relative pl-1">
          {/* Vertical Connecting Line */}
          <div className="absolute left-[19px] top-3 bottom-3 w-0.5 bg-[#F0EAE1]" />

          {/* Steps */}
          {steps.map((step, idx) => {
            const Icon = step.icon;
            const isCompleted = idx < currentStepIndex;
            const isActive = idx === currentStepIndex;
            const isAcceptedStep = step.key === 'accepted';

            return (
              <div key={step.key} className="relative flex gap-4 pb-6 last:pb-0 items-start">
                {/* Visual Step Indicator Ring */}
                <div
                  className={`w-[10px] h-[10px] rounded-full flex items-center justify-center z-10 transition-all duration-300 ${
                    isCompleted
                      ? 'bg-[#2E1513] ring-4 ring-[#2E1513]/10 scale-110'
                      : isActive
                      ? isAcceptedStep
                        ? 'bg-emerald-500 ring-[6px] ring-emerald-500/25 scale-125'
                        : 'bg-[#C27A3F] ring-[6px] ring-[#C27A3F]/25 scale-125'
                      : 'bg-[#E2D8CD] ring-2 ring-white'
                  }`}
                  style={{ marginLeft: '14px', marginTop: '6px' }}
                />

                {/* Step Card Content */}
                <div
                  className={`flex-grow flex items-center gap-3.5 px-4 py-3 rounded-2xl border transition-all duration-300 ${
                    isActive
                      ? isAcceptedStep
                        ? 'bg-emerald-50/60 border-emerald-500 shadow-[0_4px_16px_rgba(16,185,129,0.06)]'
                        : 'bg-[#FAF6F0] border-[#C27A3F] shadow-[0_4px_16px_rgba(194,122,63,0.06)]'
                      : isCompleted
                      ? 'bg-white border-[#FAF6F0] opacity-80'
                      : 'bg-white border-transparent opacity-50'
                  }`}
                >
                  <div
                    className={`w-9 h-9 rounded-xl flex items-center justify-center transition-all ${
                      isActive
                        ? isAcceptedStep
                          ? 'bg-emerald-500 text-white'
                          : 'bg-[#C27A3F] text-white'
                        : isCompleted
                        ? 'bg-[#2E1513] text-white'
                        : 'bg-[#FAF6F0] text-[#8E8075]'
                    }`}
                  >
                    <Icon className="w-5 h-5" />
                  </div>

                  <div>
                    <h4
                      className={`text-[14px] font-extrabold transition-colors ${
                        isActive ? 'text-[#2E1513]' : 'text-[#7D7067]'
                      }`}
                    >
                      {step.label}
                    </h4>
                    <p className="text-[11.5px] font-medium text-[#8E8075] leading-none mt-0.5">
                      {step.desc}
                    </p>
                  </div>

                  {isActive && (
                    <span className={`ml-auto w-2 h-2 rounded-full animate-ping ${isAcceptedStep ? 'bg-emerald-500' : 'bg-[#C27A3F]'}`} />
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Bill Receipt Details */}
      {orderDetail && (
        <div className="bg-[#FAF6F0] border border-[#EFECE6] rounded-3xl p-5 space-y-4 shadow-[inset_0_2px_4px_rgba(46,21,19,0.01)] mb-6">
          <div className="flex justify-between items-center border-b border-[#EFECE6] pb-3">
            <h4 className="text-[12px] font-extrabold text-[#2E1513] tracking-widest uppercase">
              Items Ordered
            </h4>
            <span className="text-[11px] font-bold text-[#8E8075]">
              {orderDetail.items.reduce((acc, item) => acc + item.quantity, 0)} items
            </span>
          </div>

          <div className="space-y-3">
            {orderDetail.items.map((item, idx) => (
              <div key={idx} className="flex justify-between text-[13px] leading-snug">
                <div className="pr-4">
                  <span className="font-extrabold text-[#2E1513] mr-1.5">{item.quantity}x</span>
                  <span className="font-bold text-[#7D7067]">{item.name}</span>
                  {item.customizations && item.customizations.length > 0 && (
                    <p className="text-[10px] text-[#8D5524] font-bold pl-5">
                      {item.customizations.join(', ')}
                    </p>
                  )}
                </div>
                <span className="font-extrabold text-[#2E1513] whitespace-nowrap">
                  ₹{(item.price * item.quantity).toFixed(2)}
                </span>
              </div>
            ))}
          </div>

          <div className="border-t border-[#EFECE6] pt-3.5 flex justify-between items-center text-[15px] font-extrabold text-[#2E1513]">
            <span>Total Bill Paid</span>
            <span>₹{orderDetail.total.toFixed(2)}</span>
          </div>
        </div>
      )}

      {/* Return Home Button */}
      <button
        onClick={() => {
          if (orderStatus === 'served' || (orderStatus as string) === 'rejected') {
            useCartStore.getState().setCurrentOrderId(null);
            useCartStore.getState().setOrderStatus('none');
          }
          setActiveTab('menu');
        }}
        className="w-full bg-[#FAF6F0] hover:bg-[#EFECE6] text-[#2E1513] py-4 rounded-full font-bold text-[14.5px] border border-[#EBE3D7] shadow-sm active:scale-[0.98] transition-all flex items-center justify-center gap-2"
      >
        Order More Items
        <ArrowRight className="w-4 h-4 text-[#2E1513]" />
      </button>
    </div>
  );
};
