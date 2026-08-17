$destDir = "C:\Users\Deign\Downloads\Alpha_Branding_Screenshots\Sample_Properties"
New-Item -ItemType Directory -Path $destDir -Force | Out-Null

$images = [ordered]@{
    "01_Modern_House_And_Lot_Landscape.jpg" = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=1600&auto=format&fit=crop&q=80"
    "02_Industrial_Warehouse_Landscape.jpg" = "https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=1600&auto=format&fit=crop&q=80"
    "03_Luxury_Condo_HighRise_Portrait.jpg" = "https://images.unsplash.com/photo-1545324418-cc1a3fa10c00?w=1000&auto=format&fit=crop&q=80"
    "04_Tall_Villa_Facade_Portrait.jpg"     = "https://images.unsplash.com/photo-1512917774080-9991f1c4c750?w=1000&auto=format&fit=crop&q=80"
    "05_Commercial_Office_Landscape.jpg"    = "https://images.unsplash.com/photo-1486406146926-c627a92ad1ab?w=1600&auto=format&fit=crop&q=80"
    "06_Suburban_Home_Landscape.jpg"        = "https://images.unsplash.com/photo-1568605117036-5fe5e7bab0b7?w=1600&auto=format&fit=crop&q=80"
}

foreach ($name in $images.Keys) {
    $url = $images[$name]
    $outPath = Join-Path $destDir $name
    Write-Host "Downloading $name..."
    try {
        Invoke-WebRequest -Uri $url -OutFile $outPath -UserAgent "Mozilla/5.0" -TimeoutSec 15
        $size = (Get-Item $outPath).Length
        Write-Host "  Saved $name ($size bytes)" -ForegroundColor Green
    } catch {
        Write-Host "  Download error for ${name}: $($_.Exception.Message)" -ForegroundColor Red
    }
}
