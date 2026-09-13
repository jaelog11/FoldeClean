"""백엔드가 만드는 폴더 이름의 번역. 화면 문구는 ui/i18n.js 가 맡는다. (C# I18n.cs 와 동일)"""
LANG = "ko"
SUPPORTED = ("ko", "en", "zh", "ja")
_NAMES = {
    "ko": {"documents": "문서", "images": "이미지", "videos": "동영상", "audio": "음악", "archives": "압축", "installers": "설치파일", "code": "코드", "fonts": "폰트", "shortcuts": "바로가기", "other": "기타", "dupes": "_중복", "root": "(루트)"},
    "en": {"documents": "Documents", "images": "Images", "videos": "Videos", "audio": "Music", "archives": "Archives", "installers": "Installers", "code": "Code", "fonts": "Fonts", "shortcuts": "Shortcuts", "other": "Other", "dupes": "_Duplicates", "root": "(root)"},
    "zh": {"documents": "文档", "images": "图片", "videos": "视频", "audio": "音乐", "archives": "压缩包", "installers": "安装程序", "code": "代码", "fonts": "字体", "shortcuts": "快捷方式", "other": "其他", "dupes": "_重复", "root": "(根目录)"},
    "ja": {"documents": "書類", "images": "画像", "videos": "動画", "audio": "音楽", "archives": "圧縮", "installers": "インストーラー", "code": "コード", "fonts": "フォント", "shortcuts": "ショートカット", "other": "その他", "dupes": "_重複", "root": "(ルート)"},
}


def name(key: str) -> str:
    return _NAMES.get(LANG, _NAMES["ko"]).get(key, key)


def set_lang(lang: str) -> str:
    global LANG
    if lang in SUPPORTED:
        LANG = lang
    return LANG
