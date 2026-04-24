import yfinance as yf
import pandas_ta as ta
import pandas as pd

# قائمة الأسهم المختارة (أضف ما شئت)
stocks = ["1120.SR", "2222.SR", "AAPL", "NVDA", "NFLX", "MSFT"]

print("--- SNONA SMART RADAR: RUNNING ---")

for s in stocks:
    try:
        # نطلب بيانات سنة كاملة لضمان وجود تاريخ حتى لو السوق مغلق
        df = yf.download(s, period="1y", interval="1d", progress=False)
        
        # تنظيف البيانات من أي قيم فارغة (مهم جداً للأسواق المغلقة)
        df = df.dropna()

        if df.empty or len(df) < 50:
            print(f"Stock: {s:<8} | Status: Market Closed (No recent data)")
            continue
            
        # حساب المؤشرات
        ema20 = ta.ema(df['Close'], length=20)
        ema50 = ta.ema(df['Close'], length=50)

        # أخذ آخر قيمة متوفرة فعلياً
        current_price = float(df['Close'].iloc[-1])
        last_ema20 = float(ema20.iloc[-1])
        last_ema50 = float(ema50.iloc[-1])

        # منطق تحديد الحالة
        status = "Strong Buy 🔥" if (current_price > last_ema20 and last_ema20 > last_ema50) else "Wait ⏳"
        
        print(f"Stock: {s:<8} | Price: {current_price:>8.2f} | Status: {status}")
        
    except Exception:
        print(f"Stock: {s:<8} | Status: Processing Error")

print("--- SCAN COMPLETED ---")
