"""Generates src/ASPAssistant.App/Resources/Themes/<Theme>.<Mode>.xaml from the design tokens."""
import sys, os
T = {
 ('Terminal','Dark'): dict(dark=True, bg='0D1114', bar='080B0D', barText='E4EAEC', barMuted='93A1A6', barChip='131A1D', barChipBorder='22302F',
   tabBg='0D1114', tabActive='3CCFC0', tabBar='3CCFC0', line='1A2326', border='24383A', surface='131A1D', raised='1A2427', hover='1F2D30',
   text='E4EAEC', text2='9AA9AD', accent='3CCFC0', accentText='6FE0D3', onAccent='04201C', primary='0F7F76', primaryHover='14958A',
   final='7BD35A', finalText='94E077', trans='F2994A', transText='F5AC6A', ban='F06A6A', banText='F58A8A', gold='E8C060',
   decor='1A2326', decorW=1, radius=8, rowRadius=6, num='Barlow Semi Condensed', scroll='24383A'),
 ('Terminal','Light'): dict(dark=False, bg='EEF2F2', bar='E1E8E7', barText='132020', barMuted='4F5F5E', barChip='FFFFFF', barChipBorder='C7D3D1',
   tabBg='EEF2F2', tabActive='0E8C82', tabBar='14A396', line='DCE4E3', border='C5D2D0', surface='FFFFFF', raised='E9EFEE', hover='E2EBEA',
   text='132020', text2='45555A', accent='11998D', accentText='0B7A71', onAccent='FFFFFF', primary='0F7F76', primaryHover='0B6C64',
   final='4C9A2A', finalText='3B7D1F', trans='D2721F', transText='A5560F', ban='D64545', banText='B83333', gold='B08514',
   decor='C5D2D0', decorW=1, radius=8, rowRadius=6, num='Barlow Semi Condensed', scroll='B5C4C2'),
 ('Ursus','Light'): dict(dark=False, bg='D8CFBE', bar='1C1B19', barText='EFE9DC', barMuted='ACA392', barChip='2A2825', barChipBorder='3D3A34',
   tabBg='24221F', tabActive='F3EDE0', tabBar='C8372A', tabText='B0A796', line='C9BEA9', border='B5A88F', surface='F2EDE3', raised='E6DECF', hover='E9E2D4',
   text='24211D', text2='544C40', accent='24211D', accentText='24211D', onAccent='F3EDE0', primary='24211D', primaryHover='3A352E',
   final='2F7D4E', finalText='22633D', trans='C4580F', transText='9A440A', ban='B3261E', banText='A3231B', gold='A06F00',
   decor='C8372A', decorW=3, radius=3, rowRadius=2, num='Oswald', scroll='B5A88F'),
 ('Ursus','Dark'): dict(dark=True, bg='151412', bar='0E0D0C', barText='EDE6D6', barMuted='A39A89', barChip='1F1D1A', barChipBorder='34302A',
   tabBg='1A1916', tabActive='F1EADB', tabBar='C8372A', tabText='A39A89', line='2A2723', border='3A352E', surface='1F1D1A', raised='2A2723', hover='2F2C27',
   text='EDE6D6', text2='B7AE9C', accent='E9DFC9', accentText='F1EADB', onAccent='1A1916', primary='8E2B22', primaryHover='A8352A',
   final='6DBF7E', finalText='86CF95', trans='E58A3C', transText='EFA060', ban='E0574B', banText='EE7A6F', gold='D9A93A',
   decor='C8372A', decorW=3, radius=3, rowRadius=2, num='Oswald', scroll='4A443B'),
 ('Closure','Light'): dict(dark=False, bg='F5F3FA', bar='251E3E', barText='F4F1FF', barMuted='B9B0D6', barChip='312951', barChipBorder='463D6B',
   tabBg='FFFFFF', tabActive='5B43C8', tabBar='7B61E0', tabText='5E5779', line='E9E5F3', border='D8D1EA', surface='FFFFFF', raised='F3F0FA', hover='F0ECFA',
   text='221C38', text2='4F4868', accent='6A52D9', accentText='5640BF', onAccent='FFFFFF', primary='6A52D9', primaryHover='5A43C4',
   final='2A9A62', finalText='1B7549', trans='DD7230', transText='A95318', ban='D6454C', banText='B8333A', gold='BC8E1C',
   decor='E58AB8', decorW=2, radius=10, rowRadius=8, num='Silkscreen', scroll='CFC6E6'),
 ('Closure','Dark'): dict(dark=True, bg='16131F', bar='0F0C17', barText='EEEAFB', barMuted='A49DC0', barChip='1F1A2C', barChipBorder='332B48',
   tabBg='16131F', tabActive='B6A4FF', tabBar='9B84F5', line='241F33', border='332C47', surface='1D1929', raised='262136', hover='2B2540',
   text='EEEAFB', text2='B3ACCD', accent='9B84F5', accentText='BCAEFF', onAccent='16102E', primary='6A52D9', primaryHover='7C66E3',
   final='6CCB94', finalText='86D8A8', trans='F09A5A', transText='F5AE77', ban='EE6B74', banText='F48C93', gold='E2BC5C',
   decor='E58AB8', decorW=2, radius=10, rowRadius=8, num='Silkscreen', scroll='3E3657'),
}
P = 'pack://application:,,,/ASPAssistant.App;component/Resources/Fonts/'
FONT = {'Barlow Semi Condensed': P + '#Barlow Semi Condensed',
        'Oswald': P + '#Oswald, ' + P + '#Oswald SemiBold',
        'Silkscreen': P + '#Silkscreen'}
def a(alpha, hex_): return f'#{alpha:02X}{hex_}'
def gen(t):
    d = t['dark']; n = (lambda al: a(al, 'FFFFFF')) if d else (lambda al: a(al, '000000'))
    A = t['accent']; B = {}
    B['TitleBackgroundBrush'] = '#FF'+t['bar']
    B['PanelBackgroundBrush'] = '#FF'+t['bg']
    B['InputBackgroundBrush'] = '#FF'+t['surface']
    B['CardBackgroundBrush'] = '#FF'+t['surface']
    B['SectionBackgroundBrush'] = '#FF'+t['raised']
    B['CardHoverBrush'] = '#FF'+t['hover']
    B['SummaryBackgroundBrush'] = a(0xE6, t['surface'])
    B['ToastBackgroundBrush'] = a(0xF0, t['bar'])
    B['MaskCardBackgroundBrush'] = a(0xF2, t['surface'])
    for k, al in [('AccentFaint',0x0A),('AccentSubtle',0x0D),('AccentSoft',0x1A),('AccentTint',0x22),('AccentHover',0x33),('AccentMedium',0x44),('AccentBorder',0x66),('AccentStrongBorder',0x88),('AccentPressed',0xAA)]:
        B[k+'Brush'] = a(al, A)
    B['FilterSelectedBrush'] = '#FF'+A
    B['FilterSelectedTintBrush'] = a(0x1F, A)
    B['FilterSelectedSoftBrush'] = a(0x33, A)
    for k, al in [('NeutralFaint',0x0A if d else 0x08),('ScrollTrack',0x0D if d else 0x08),('SoftBorder',0x11 if d else 0x0F),('NeutralTint',0x14 if d else 0x0A),('NeutralSurface',0x1A if d else 0x0D),('NeutralHover',0x22 if d else 0x12),('NeutralPressed',0x2A if d else 0x1A),('NeutralStrong',0x33)]:
        B[k+'Brush'] = n(al)
    B['HeaderSurfaceBrush'] = a(0x11,'000000') if d else a(0xD9,'FFFFFF')
    B['InputOverlayBrush'] = a(0x22,'000000') if d else '#FFFFFFFF'
    B['ScrollThumbBrush'] = '#FF'+t['scroll']
    B['EliteBrush'] = '#FF'+t['gold']; B['EliteTintBrush'] = a(0x1F, t['gold'])
    B['DangerBrush'] = '#FF'+t['banText']; B['DangerMutedBrush'] = a(0xCC, t['ban'])
    for k, al in [('DangerTint',0x33),('DangerSoft',0x1F),('DangerBorder',0x66),('DangerHover',0x88),('DangerPressed',0xAA)]:
        B[k+'Brush'] = a(al, t['ban'])
    B['SuccessBrush'] = '#FF'+t['final']; B['SuccessHoverBrush'] = a(0xDD, t['final']); B['SuccessTextBrush'] = '#FF'+t['finalText']
    B['SuccessTintBrush'] = a(0x1F, t['final']); B['SuccessBorderBrush'] = a(0x88, t['final'])
    B['WarningBrush'] = '#FF'+t['trans']; B['WarningTextBrush'] = '#FF'+t['transText']; B['WarningMutedBrush'] = a(0xCC, t['trans']); B['WarningTintBrush'] = a(0x1F, t['trans'])
    B['PrimaryActionBrush'] = '#FF'+t['primary']; B['PrimaryActionHoverBrush'] = '#FF'+t['primaryHover']
    B['AccentBrush'] = '#FF'+t['accentText']
    B['TrackingBrush'] = '#FF'+t['final']
    B['TextPrimaryBrush'] = '#FF'+t['text']; B['TextSecondaryBrush'] = '#FF'+t['text2']
    B['BorderBrush'] = '#FF'+t['border']; B['OnAccentBrush'] = '#FF'+t['onAccent']
    B['AvailableBrush'] = '#FF'+t['finalText']; B['AvailableTintBrush'] = a(0x1F, t['final'])
    # New keys (redesign)
    B['AccentSolidBrush'] = '#FF'+A
    B['FinalBrush'] = '#FF'+t['final']; B['FinalTintBrush'] = a(0x1F, t['final'])
    B['TransitionBrush'] = '#FF'+t['trans']; B['TransitionTextBrush'] = '#FF'+t['transText']; B['TransitionTintBrush'] = a(0x1F, t['trans'])
    B['LineBrush'] = '#FF'+t['line']
    B['TitleTextBrush'] = '#FF'+t['barText']; B['TitleMutedBrush'] = '#FF'+t['barMuted']
    B['TitleChipBrush'] = '#FF'+t['barChip']; B['TitleChipBorderBrush'] = '#FF'+t['barChipBorder']
    B['TitleDecorBrush'] = '#FF'+t['decor']
    B['TabBarBackgroundBrush'] = '#FF'+t['tabBg']
    B['TabTextBrush'] = '#FF'+t.get('tabText', t['text2'])
    B['TabActiveBrush'] = '#FF'+t['tabActive']; B['TabActiveBarBrush'] = '#FF'+t['tabBar']
    B['TabActiveBackgroundBrush'] = a(0x12, t['tabBar'])
    # Hover on an unselected tab: readable on the tab bar itself (乌萨斯's bar is dark even in 日间)
    hover_text = t['tabActive'] if t.get('tabText') and t['tabBg'] in ('24221F', '1A1916') else t['text']
    B['TabHoverTextBrush'] = '#FF'+hover_text
    B['TabHoverBackgroundBrush'] = a(0x14, hover_text)
    lines = ['<!-- Generated by tools/gen_themes.py from the redesign tokens; edit the script, not this file. -->',
             '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
             '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
             '                    xmlns:sys="clr-namespace:System;assembly=mscorlib">']
    for k, v in B.items(): lines.append(f'    <SolidColorBrush x:Key="{k}" Color="{v}" />')
    lines.append(f'    <Color x:Key="RowGradientStartColor">#FF{t["surface"]}</Color>')
    lines.append(f'    <Color x:Key="RowGradientMiddleColor">#F0{t["surface"]}</Color>')
    lines.append(f'    <Color x:Key="RowGradientEndColor">#40{t["surface"]}</Color>')
    lines.append(f'    <CornerRadius x:Key="CardCornerRadius">{t["radius"]}</CornerRadius>')
    lines.append(f'    <CornerRadius x:Key="RowCornerRadius">{t["rowRadius"]}</CornerRadius>')
    # Avatars are rounded squares (≈ size / 4; near-square in the angular Ursus skin), pills follow the row radius.
    square = t["radius"] <= 3
    lines.append(f'    <CornerRadius x:Key="AvatarCornerRadius">{2 if square else 7}</CornerRadius>')
    lines.append(f'    <CornerRadius x:Key="AvatarSmallCornerRadius">{2 if square else 5}</CornerRadius>')
    lines.append(f'    <CornerRadius x:Key="PillCornerRadius">{2 if square else t["rowRadius"]}</CornerRadius>')
    lines.append(f'    <CornerRadius x:Key="DotCornerRadius">{1 if square else 2}</CornerRadius>')
    lines.append(f'    <sys:Double x:Key="RowRadiusValue">{t["rowRadius"]}</sys:Double>')
    lines.append(f'    <Thickness x:Key="TitleDecorThickness">0,0,0,{t["decorW"]}</Thickness>')
    lines.append(f'    <FontFamily x:Key="NumberFontFamily">{FONT[t["num"]]}, Segoe UI, Microsoft YaHei UI</FontFamily>')
    lines.append('</ResourceDictionary>')
    return '\n'.join(lines) + '\n'
out = sys.argv[1]
for (th, mode), t in T.items():
    with open(os.path.join(out, f'{th}.{mode}.xaml'), 'w', encoding='utf-8') as f: f.write(gen(t))
print('ok')
