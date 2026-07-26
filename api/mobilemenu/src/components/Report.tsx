import React from 'react';
import { ClipboardList, Clock, CheckCircle2, ChevronRight } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';

export const Report: React.FC = () => {
  const { orderHistory, currentOrderId, orderStatus, setActiveTab } = useCartStore();

  const activeOrder = orderHistory.find((o) => o.id === currentOrderId);

  // Format Date & Time cleanly
  const formatDateTime = (isoString: string) => {
    try {
      const date = new Date(isoString);
      return date.toLocaleDateString('en-US', {
        day: 'numeric',
        month: 'short',
        year: 'numeric'
      }) + ' • ' + date.toLocaleTimeString('en-US', {
        hour: '2-digit',
        minute: '2-digit',
        hour12: true
      });
    } catch (e) {
      return 'Just now';
    }
  };

  // English Status tags
  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'pending':
        return (
          <span className="bg-[#FEF3C7] text-[#D97706] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#FCD34D]">
            Pending
          </span>
        );
      case 'accepted':
        return (
          <span className="bg-[#DBEAFE] text-[#2563EB] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#BFDBFE]">
            Accepted
          </span>
        );
      case 'preparing':
        return (
          <span className="bg-[#F3E8FF] text-[#7C3AED] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#E9D5FF] animate-pulse">
            Preparing
          </span>
        );
      case 'ready':
        return (
          <span className="bg-[#D1FAE5] text-[#059669] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#A7F3D0] animate-bounce">
            Ready
          </span>
        );
      case 'served':
        return (
          <span className="bg-[#E0F2FE] text-[#0284C7] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#BAE6FD]">
            Served
          </span>
        );
      default:
        return (
          <span className="bg-[#F3F4F6] text-[#4B5563] text-[11px] font-black px-2.5 py-1 rounded-full border border-[#E5E7EB]">
            Processed
          </span>
        );
    }
  };

  return (
    <div className="px-5 pb-36 font-nunito animate-[fadeIn_0.4s_ease-out]">
      <h2 className="text-[20px] font-extrabold text-[#2E1513] border-b border-[#F4EFEA] pb-3 mb-5">
        Order Report & History
      </h2>

      {/* SECTION 1: ACTIVE ORDER STATUS */}
      <div className="mb-8">
        <h3 className="text-[12px] font-black text-[#7D7067] uppercase tracking-widest pl-1 mb-3">
          Active Order
        </h3>

        {activeOrder && orderStatus !== 'served' ? (
          <div className="bg-[#FAF6F0] border-2 border-[#C27A3F] rounded-2.5xl p-5 shadow-[0_8px_24px_-8px_rgba(194,122,63,0.12)] space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <span className="text-[10px] font-black text-[#C27A3F] uppercase tracking-wider block">
                  Table {activeOrder.tableNumber}
                </span>
                <span className="text-[13px] text-[#7D7067] font-bold">
                  ID: #{activeOrder.id.slice(-6).toUpperCase()}
                </span>
              </div>
              {getStatusBadge(orderStatus)}
            </div>

            {/* Active Items list snippet */}
            <div className="border-t border-b border-[#F2ECE4] py-3.5 space-y-2">
              {activeOrder.items.map((item) => (
                <div key={item.id} className="flex justify-between text-[14px] font-bold text-[#2E1513]">
                  <span>
                    {item.name} <span className="text-[#8E8075] text-[12px]">x{item.quantity}</span>
                  </span>
                  <span>₹{(item.price * item.quantity).toFixed(0)}</span>
                </div>
              ))}
            </div>

            <div className="flex items-center justify-between pt-1">
              <div>
                <span className="text-[11px] text-[#7D7067] font-extrabold block">Total Amount</span>
                <span className="text-[18px] font-black text-[#2E1513]">₹{activeOrder.total.toFixed(0)}</span>
              </div>

              <button
                onClick={() => setActiveTab('status')}
                className="bg-[#2E1513] text-white text-[12.5px] font-black px-4 py-2.5 rounded-full flex items-center gap-1.5 active:scale-95 transition-transform shadow-md cursor-pointer"
              >
                Track Live
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        ) : (
          <div className="bg-white border border-[#FAF6F0] rounded-2.5xl p-5 text-center text-[#8E8075] py-7 shadow-[0_4px_16px_rgba(46,21,19,0.02)] space-y-2">
            <CheckCircle2 className="w-8 h-8 text-[#A7F3D0] mx-auto" />
            <p className="font-extrabold text-[14.5px] text-[#2E1513]">No active order</p>
            <p className="text-[12px] max-w-[220px] mx-auto font-medium">
              Your active order details and tracking status will appear here when you place an order.
            </p>
          </div>
        )}
      </div>

      {/* SECTION 2: PREVIOUS ORDERS HISTORY */}
      <div>
        <h3 className="text-[12px] font-black text-[#7D7067] uppercase tracking-widest pl-1 mb-3">
          Order History
        </h3>

        {orderHistory.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center text-[#8E8075] space-y-4">
            <div className="w-16 h-16 bg-white border border-[#FAF6F0] rounded-full flex items-center justify-center shadow-[0_4px_16px_rgba(46,21,19,0.02)]">
              <ClipboardList className="w-7 h-7 text-[#8E8075]" />
            </div>
            <div className="space-y-1">
              <p className="font-extrabold text-[15px] text-[#2E1513]">No order history yet</p>
              <p className="text-[12px] max-w-[200px] leading-relaxed mx-auto font-medium">
                Your past receipts and dining history will be saved and displayed here.
              </p>
            </div>
            <button
              onClick={() => setActiveTab('menu')}
              className="bg-[#2E1513] text-white font-black text-[13px] px-5 py-2.5 rounded-full shadow-md active:scale-95 transition-transform cursor-pointer"
            >
              View Menu
            </button>
          </div>
        ) : (
          <div className="space-y-4">
            {orderHistory.map((order, idx) => (
              <div
                key={order.id + idx}
                className="bg-white border border-[#FAF6F0] rounded-2.5xl p-5 shadow-[0_6px_20px_rgba(46,21,19,0.03)] space-y-3.5"
              >
                {/* History Item Header */}
                <div className="flex justify-between items-start border-b border-[#FAF6F0] pb-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-[13px] font-black text-[#2E1513]">Table {order.tableNumber}</span>
                      <span className="text-[9px] bg-[#FAF6F0] text-[#7D7067] px-1.5 py-0.5 rounded border border-[#EFECE6] font-bold">
                        #{order.id.slice(-5).toUpperCase()}
                      </span>
                    </div>
                    
                    <div className="flex items-center gap-1 text-[11px] text-[#8E8075] mt-1 font-bold">
                      <Clock className="w-3 h-3" />
                      <span>{formatDateTime(order.created)}</span>
                    </div>
                  </div>
                  
                  {getStatusBadge(order.status)}
                </div>

                {/* Items Breakdown list */}
                <div className="space-y-2 py-0.5">
                  {order.items.map((item) => (
                    <div key={item.id} className="flex justify-between text-[13px] font-bold text-[#7D7067]">
                      <span>
                        {item.name} <span className="text-[11px] text-[#8E8075]">x{item.quantity}</span>
                      </span>
                      <span className="text-[#2E1513]">₹{(item.price * item.quantity).toFixed(0)}</span>
                    </div>
                  ))}
                </div>

                {/* Receipt Footer */}
                <div className="flex justify-between items-center border-t border-[#FAF6F0] pt-3 mt-1">
                  <span className="text-[12px] text-[#8E8075] font-extrabold">Total Paid</span>
                  <span className="text-[15.5px] font-black text-[#C27A3F]">
                    ₹{order.total.toFixed(0)}
                  </span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
