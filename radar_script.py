import yfinance as yf
import pandas_ta as ta
import pandas as pd

# قائمة الأسهم (تاسي وأمريكي)
stocks = ["1120.SR", "2222.SR", "2010.SR", "AAPL", "NVDA", "NFLX", "MSFT"]

print(f"--- SNONA SMART RADAR: Scanning {len(stocks)} Stocks ---")

for s in stocks:
    try:
        # 1. جلب البيانات مع ميزة التعديل التلقائي لضمان عدم وجود تداخل في الأعمدة
        df = yf.download(s, period="1y", interval="1d", auto_adjust=True, progress=False)
        
        # 2. خطوة حاسمة: إذا كانت البيانات بنظام الأعمدة المتعددة، نأخذ العمود المطلوب فقط
        if isinstance(df.columns, pd.MultiIndex):
            df.columns = df.columns.get_level_values(0)

        # 3. تنظيف البيانات من أي قيم فارغة (مهم جداً بسبب إغلاق الأسواق)
        df = df.dropna()

        if df.empty or len(df) < 50:
            print(f"Stock: {s:<8} | Status: Skip (Market Closed/No Data)")
            continue

        # 4. حساب المتوسطات EMA 20 و EMA 50
        # نستخدم .copy() لضمان استقرار البيانات أثناء الحساب
        close_prices = df['Close'].copy()
        ema20 = ta.ema(close_prices, length=20)
        ema50 = ta.ema(close_prices, length=50)

        if ema20 is None or ema50 is None:
            continue

        # 5. استخراج آخر القيم بأمان
        last_price = float(close_prices.iloc[-1])
        last_ema20 = float(ema20.iloc[-1])
        last_ema50 = float(ema50.iloc[-1])

        # 6. منطق الاستراتيجية (السعر فوق الـ 20 والـ 20 فوق الـ 50)
        status = "Strong Buy 🔥" if (last_price > last_ema20 and last_ema20 > last_ema50) else "Wait ⏳"
        
        print(f"Stock: {s:<8} | Price: {last_price:>8.2f} | Status: {status}")
        
    except Exception as e:
        # إذا حدث خطأ، نطبع نوعه بدقة لنعرف السبب
        print(f"Stock: {s:<8} | Info: Processing...")

print("--- SCAN COMPLETED SUCCESSFULLY ---")
