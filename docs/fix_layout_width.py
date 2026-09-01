# fix_layout_width.py — 生成後の設計書 HTML の幅制限を外す
#
# 目的:
#   gen_design_html.py は本文幅を 4 箇所で制限しており、そのままではノート PC でも
#   右側に大きな余白が残る。とくに地の文の `78ch` は和文では 39 文字ぶんしかなく、
#   表（1180px）と地の文（約507px）で同じ文書内の幅がばらつく。
#   4 箇所すべてを max-width:none にし、article の左右 padding だけを残す。
#
# 使い方:
#   python fix_layout_width.py <設計書.html>
#
# 注意（過去に踏んだ罠）:
#   1) 単純な文字列置換にしない。設計書の本文がこの不具合の説明として
#      "max-width:78ch" 等を引用していると、説明文まで書き換わる。
#      セレクタを含む形で対象を特定する。
#   2) 置換は <style> 内の「画面表示用」領域だけに限定する。@media print 側は
#      既に max-width:none であり、領域を限定しないと「修正済み」と誤判定する。
#   3) 画面表示用の領域は "@media print{"（波括弧まで）で切る。"@media print" だけだと
#      CSS のコメント文に含まれる同じ文字列にヒットして領域が切り詰められる。
import io
import re
import sys

# 画面表示用 CSS と印刷用 CSS の境界。波括弧まで含める（注意 3）
PRINT_MARKER = "@media print{"

# (ラベル, 置換前の候補, 置換後) の並び。候補は生成器の版差を吸収するため複数持つ
TARGETS = [
    ("本文の外枠", [".wrap{max-width:1180px}"], ".wrap{max-width:none}"),
    ("地の文",
     [".wrap>p,.wrap>ul,.wrap>ol{max-width:78ch}",
      ".wrap>p,.wrap>ul,.wrap>ol{max-width:78em}"],
     ".wrap>p,.wrap>ul,.wrap>ol{max-width:none}"),
    ("注記帯", ["border-radius:0 6px 6px 0;max-width:1000px;"],
     "border-radius:0 6px 6px 0;max-width:none;"),
    ("コードブロック", ["background:#141a21;max-width:1000px;scroll-margin-top:12px}"],
     "background:#141a21;max-width:none;scroll-margin-top:12px}"),
]


def split_css(html):
    """HTML を (style 前, 画面用 CSS, 印刷用 CSS + 以降) の 3 つに分ける。"""
    m = re.search(r"<style[^>]*>(.*?)</style>", html, re.S)
    if not m:
        return html, "", ""
    a, b = m.start(1), m.end(1)
    i = m.group(1).find(PRINT_MARKER)
    boundary = a + (i if i >= 0 else len(m.group(1)))
    return html[:a], html[a:boundary], html[boundary:]


def fix(path):
    """幅制限を解除する。成功で 0、想定外の状態なら 1 を返す。"""
    html = io.open(path, encoding="utf-8").read()
    head, screen, tail = split_css(html)
    if not screen:
        print("[NG] <style> が見つからない", file=sys.stderr)
        return 1
    done, skipped, ng = [], [], []
    for label, befores, after in TARGETS:
        hit = [x for x in befores if x in screen]
        if len(hit) == 1 and screen.count(hit[0]) == 1:
            screen = screen.replace(hit[0], after, 1)
            done.append(label)
        elif not hit and after in screen:
            skipped.append(label)
        else:
            ng.append("%s: 想定外（候補 %d 件）" % (label, len(hit)))
    if ng:
        print("[NG] " + " / ".join(ng), file=sys.stderr)
        return 1
    # 画面表示用 CSS に数値指定の max-width が残っていないことを確認する
    rest = re.findall(r"max-width:\s*([0-9.]+)(px|em|ch)", screen)
    if rest:
        print("[NG] 幅の上限が残っている: %s"
              % ", ".join(a + b for a, b in rest), file=sys.stderr)
        return 1
    io.open(path, "w", encoding="utf-8", newline="\n").write(head + screen + tail)
    print("[OK] 幅制限を解除: %s" % (" / ".join(done) or "（すでに解除済み）"))
    return 0


sys.exit(fix(sys.argv[1]))
