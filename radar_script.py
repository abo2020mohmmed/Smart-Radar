import yfinance as yf
import pandas_ta as ta
import pandas as pd

# قائمة الأسهم (تاسي وأمريكي)
stocks = [
    "1120.SR", "2222.SR", "2010.SR", "1180.SR", "1150.SR", # تاسي
    "AAPL", "NVDA", "NFLX", "MSFT", "TSLA", "GOOGL"       # أمريكي
]

print(f"--- SNONA SMART RADAR: Scanning {len(stocks)} Stocks ---")

for s in stocks:
    try:
        # جلب بيانات كافية (6 أشهر) لضمان الحساب الصحيح للمؤشرات
        df = yf.download(s, period="6mo", interval="1d", progress=False)
        
        # فحص إذا كانت البيانات فارغة أو غير كافية
        if df is None or df.empty or len(df) < 50:
            print(f"Stock: {s:<8} | Status: Market Closed or No Data")
            continue

        # حساب المتوسطات EMA 20 و EMA 50
        # نستخدم .squeeze() للتأكد من أننا نتعامل مع أرقام بسيطة
        close_series = df['Close'].squeeze()
        ema20 = ta.ema(close_series, length=20)
        ema50 = ta.ema(close_series, length=50)

        if ema20 is None or ema50 is None:
            continue

        last_price = float(close_series.iloc[-1])
        last_ema20 = float(ema20.iloc[-1])
        last_ema50 = float(ema50.iloc[-1])

        # منطق الاستراتيجية الخاصة بك
        status = "Strong Buy 🔥" if (last_price > last_ema20 and last_ema20 > last_ema50) else "Wait ⏳"
        
        print(f"Stock: {s:<8} | Price: {last_price:>8.2f} | Status: {status}")
        
    except Exception as e:
        print(f"Stock: {s:<8} | Status: Error in analysis")

print("--- Scan Completed ---")
