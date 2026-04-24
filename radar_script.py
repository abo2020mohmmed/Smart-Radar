import yfinance as yf
import pandas_ta as ta

# قائمة الأسهم المختارة (تاسي وأمريكي)
stocks = ["1120.SR", "2222.SR", "AAPL", "NVDA", "NFLX"]

print("--- SNONA SMART RADAR: Scanning ---")

for s in stocks:
    try:
        # جلب البيانات لفريم اليوم
        df = yf.download(s, period="6mo", interval="1d")
        
        # حساب المؤشرات كما في خوارزميتك (EMA 20 & 50)
        ema20 = ta.ema(df['Close'], length=20).iloc[-1]
        ema50 = ta.ema(df['Close'], length=50).iloc[-1]
        last_price = df['Close'].iloc[-1]

        # تحديد الحالة بناءً على منطق SMART WAY
        status = "Strong Buy 🔥" if (last_price > ema20 and ema20 > ema50) else "Wait ⏳"
        
        print(f"Stock: {s} | Price: {last_price:.2f} | Status: {status}")
    except Exception as e:
        print(f"Error checking {s}: {e}")
