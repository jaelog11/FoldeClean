"""테스트용 가짜 폴더 생성. 실제 자료는 전혀 쓰지 않는다.

py -3.11 make_sample.py [폴더] [파일수]
"""
import os
import random
import sys
import time

EXTS = ["pdf", "docx", "xlsx", "hwp", "txt", "jpg", "png", "heic", "mp4", "mov", "mp3", "zip", "7z",
        "exe", "msi", "py", "js", "json", "ttf", "csv", "pptx", "bin", "lnk"]
WORDS = ["보고서", "회의록", "사진", "스크린샷", "설치", "백업", "초안", "최종", "final", "v2", "복사본", "invoice", "memo", "scan"]
SUBDIRS = ["", "", "", "작업중", "old", "다운로드정리", "사진/2024", "사진/2025", "프로젝트A", "프로젝트A/src"]


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "sample_data")
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 600
    random.seed(7)
    os.makedirs(root, exist_ok=True)
    now = time.time()
    # 프로젝트 폴더 표식
    os.makedirs(os.path.join(root, "프로젝트A", "src"), exist_ok=True)
    open(os.path.join(root, "프로젝트A", "package.json"), "w").write("{}")
    # 프로그램 폴더 (exe + dll)
    pd = os.path.join(root, "SomeTool")
    os.makedirs(pd, exist_ok=True)
    open(os.path.join(pd, "tool.exe"), "wb").write(b"MZ" + b"\0" * 100)
    open(os.path.join(pd, "core.dll"), "wb").write(b"MZ" + b"\0" * 100)
    payloads = [os.urandom(random.randint(200, 4000)) for _ in range(40)]  # 중복 만들기 위한 풀
    for i in range(n):
        sub = random.choice(SUBDIRS)
        d = os.path.join(root, sub) if sub else root
        os.makedirs(d, exist_ok=True)
        ext = random.choice(EXTS)
        name = f"{random.choice(WORDS)}_{i}.{ext}"
        p = os.path.join(d, name)
        data = random.choice(payloads) if random.random() < 0.25 else os.urandom(random.randint(100, 20000))
        with open(p, "wb") as f:
            f.write(data)
        age_days = random.choice([random.uniform(0, 20), random.uniform(20, 200), random.uniform(200, 1200)])
        t = now - age_days * 86400
        os.utime(p, (t, t))
    print(f"created {n} files under {root}")


if __name__ == "__main__":
    main()
