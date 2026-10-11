"""Rebuilds docs/Alpha_Branding_Studio_User_Manual.pdf from user-manual.html.

Usage: python docs/manual/build.py [--screenshots]
  --screenshots  re-capture docs/manual/images from the app before rendering.

Pass 1 renders the pages, fill_toc.py fills the contents page numbers, pass 2 renders the final PDF.
"""
import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
OUT = HERE.parent / "Alpha_Branding_Studio_User_Manual.pdf"
EDGE = Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")


def render(html: Path, pdf: Path, profile: Path) -> None:
    subprocess.run(
        [str(EDGE), "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
         "--generate-pdf-document-outline", f"--user-data-dir={profile}",
         f"--print-to-pdf={pdf}", html.as_uri()],
        check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if not pdf.exists() or pdf.stat().st_size == 0:
        sys.exit(f"Edge did not produce {pdf}")


def main() -> None:
    if not EDGE.exists():
        sys.exit(f"Microsoft Edge not found at {EDGE}")

    if "--screenshots" in sys.argv[1:]:
        env = dict(os.environ, ALPHA_BRANDING_MANUAL_SCREENSHOTS="1")
        subprocess.run(
            ["dotnet", "test", str(REPO / "tests" / "Alpha.Branding.Tests" / "Alpha.Branding.Tests.csproj"),
             "--filter", "FullyQualifiedName~ManualScreenshotGenerator", "--nologo"],
            check=True, env=env)

    source = HERE / "user-manual.html"
    filled = HERE / ".render.html"  # must sit next to the source so relative image paths resolve
    with tempfile.TemporaryDirectory() as work:
        work = Path(work)
        try:
            render(source, work / "pass1.pdf", work / "profile")
            subprocess.run([sys.executable, str(HERE / "fill_toc.py"), str(work / "pass1.pdf"),
                            str(source), str(filled)], check=True)
            render(filled, OUT, work / "profile")
        finally:
            filled.unlink(missing_ok=True)
    print(f"Built {OUT}")


if __name__ == "__main__":
    main()
