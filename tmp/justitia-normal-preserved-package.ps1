$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath 'ConceptArt\JustitiaHardNormalPreserved_v2').Path
$normal=(Resolve-Path -LiteralPath 'ConceptArt\CharacterMatched\Stage5\01-justitia-normal-v1.png').Path
$utf8=New-Object System.Text.UTF8Encoding($false)
[IO.Directory]::CreateDirectory((Join-Path $root 'Reference')) | Out-Null
Copy-Item -LiteralPath $normal -Destination (Join-Path $root 'Reference\01-normal.png')
$dirs=@('A-imbalanced','B-unsealed','C-fractured')
$desc=@('기울어진 대형 저울과 무거운 추. 넓어진 예복과 균열로 판결의 무게를 강조.','노멀에 가까운 자세. 갈라진 흉부 외피 사이로 가는 시계축이 드러나는 변형.','한쪽 외피의 분절과 구속 사슬. 비대칭 예복으로 긴장감을 만든 변형.')
$rows=@()
$promptParts=New-Object System.Collections.Generic.List[string]
for($i=0;$i -lt $dirs.Count;$i++){
  $d=$dirs[$i]
  $m=Get-Content -LiteralPath (Join-Path $root ($d+'\manifest.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
  $x=$m.images[0]
  $file=Join-Path $root ($d+'\'+$x.image)
  $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLower()
  if($hash -ne $x.sha256){throw "Image hash mismatch: $d"}
  $x.image=$d+'/'+$x.image
  $x.prompts=@($x.prompts | ForEach-Object {$d+'/'+$_})
  $x | Add-Member -NotePropertyName number -NotePropertyValue ($i+1)
  $x | Add-Member -NotePropertyName description_ko -NotePropertyValue $desc[$i]
  $rows+=$x
  foreach($p in $x.prompts){
    $promptParts.Add(('===== '+$p+' ====='+[Environment]::NewLine+(Get-Content -LiteralPath (Join-Path $root $p) -Raw -Encoding UTF8)))
  }
}
$manifest=[ordered]@{
  schema_version=1
  title_ko='유스티티아 하드 — 노멀 계승 3안'
  created='2026-11-15'
  mode='built-in image_gen'
  scope='Three full-body HARD concepts derived solely from NORMAL Justitia identity. Comparison candidates, not selected/fixed.'
  normal_reference=[ordered]@{image='Reference/01-normal.png';original='ConceptArt/CharacterMatched/Stage5/01-justitia-normal-v1.png';role='sole initial identity/style reference';sha256=(Get-FileHash -LiteralPath $normal -Algorithm SHA256).Hash.ToLower()}
  images=$rows
}
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'),($manifest | ConvertTo-Json -Depth 20),$utf8)
[IO.File]::WriteAllText((Join-Path $root 'prompts-all.txt'),($promptParts -join ([Environment]::NewLine+[Environment]::NewLine)),$utf8)
$readme=@'
유스티티아 하드 — 노멀 계승 3안 (2026-11-15)

노멀 원본의 가는 체형, 물결 머리, 안대, 높은 칼라, 별 장식의 긴 예복, 대검과 두 접시 저울을 기준으로 제작했습니다.
몸의 비대화 대신 저울의 하중, 외피의 개방과 균열, 분절된 예복으로 하드 변형을 표현했습니다.

01 / 무너진 균형 (A-imbalanced/01-boss.png)
기울어진 대형 저울, 무거운 추, 넓어진 예복과 균열.

02 / 개방된 판결 (B-unsealed/01-boss.png)
노멀에 가까운 자세, 두 흉부 외피 사이의 좁은 시계축, 높이가 다른 접시.

03 / 봉인 균열 (C-fractured/01-boss.png)
한쪽 외피의 개방, 팔과 골반에 연결된 구속 사슬, 비대칭 예복.

INDEX.html: 노멀 원본과 하드 3안 비교. 이미지를 누르면 PNG 원본을 엽니다.
Reference/01-normal.png: 비교용 노멀 원본 복사본.
각 폴더: 1536×1024 PNG, 정확한 최초 프롬프트와 여백 수정 프롬프트, 개별 manifest.
prompts-all.txt: 프롬프트 전체.
manifest.json: 이미지별 생성 출처, 크기, SHA256, 참조 역할 및 시각 검토 내용.

제작: built-in image_gen. 각 안은 노멀 원본만을 최초 이미지 입력으로 사용했습니다.
전신 여백을 확보하기 위해 각 안에 한 번씩 구도 수정을 적용했습니다.
원본 노멀, 기존 하드, 이전 탐색안 및 확정 갤러리는 덮어쓰지 않았습니다.
현재 3안은 비교 후보입니다. 최종 선택은 아직 반영하지 않았습니다.
'@
[IO.File]::WriteAllText((Join-Path $root 'README.txt'),$readme,$utf8)
$css=@'
:root{color-scheme:light dark}*{box-sizing:border-box}body{margin:0;font:14px/1.65 "Segoe UI","Malgun Gothic",sans-serif;background:var(--background,#f6f4fa);color:var(--foreground,#292637)}main{max-width:1460px;margin:auto;padding:28px}h1{font-size:24px;line-height:1.3;margin:0 0 10px}p{margin:0 0 16px;color:var(--muted-foreground,#696477)}.eyebrow{font-size:11px;letter-spacing:.16em;color:var(--muted-foreground,#716a82);margin-bottom:8px}.toolbar{display:flex;gap:7px;flex-wrap:wrap;margin:22px 0 16px}button{border:1px solid var(--border,#d6cfe2);background:var(--card,#fff);color:var(--foreground,#292637);font:inherit;padding:7px 14px;border-radius:20px;cursor:pointer}button[aria-pressed=true],button:hover{background:var(--accent,#ddd3f2);color:var(--accent-foreground,#2b2144)}.cards{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}.card{background:var(--card,#fff);border:1px solid var(--border,#ded8e8);border-radius:16px;overflow:hidden}.card[hidden]{display:none}.art{display:block;line-height:0;background:#eee6fa}.art img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}.copy{padding:17px 18px}h2{font-size:18px;margin:0 0 7px}.num{color:var(--muted-foreground,#756a8a);font-size:12px;margin-bottom:4px}.copy p{font-size:13px;margin:0}.cards.focus{grid-template-columns:1fr}.cards.focus .art img{max-height:740px}details{margin-top:20px;border:1px solid var(--border,#ded8e8);background:var(--card,#fff);border-radius:12px;padding:14px}summary{cursor:pointer}details img{display:block;width:100%;max-width:980px;height:auto;margin:14px auto 0}.foot{font-size:12px;margin-top:18px;color:var(--muted-foreground,#746d82)}@media(max-width:640px){main{padding:18px}.cards{grid-template-columns:1fr}h1{font-size:21px}}
'@
$js=@'
document.querySelectorAll('.toolbar button').forEach(b=>b.addEventListener('click',()=>{const id=b.dataset.view;document.querySelectorAll('.toolbar button').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));document.querySelector('.cards').classList.toggle('focus',id!=='all');document.querySelectorAll('.card').forEach(x=>x.hidden=id!=='all'&&x.dataset.id!==id)}));
'@
function Build-Html($sources,$normalSrc,$inline){
  $cards=New-Object System.Collections.Generic.List[string]
  for($i=0;$i -lt 3;$i++){
    $x=$rows[$i];$src=$sources[$i];$n=$i+1
    $cards.Add(('<article class="card" data-id="'+$n+'"><a class="art" href="'+$src+'" target="_blank" rel="noopener"><img loading="eager" decoding="sync" src="'+$src+'" alt="'+$x.title_ko+' 전신"></a><div class="copy"><div class="num">0'+$n+' / HARD</div><h2>'+$x.title_ko+'</h2><p>'+$x.description_ko+'</p></div></article>'))
  }
  return '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>유스티티아 하드 — 노멀 계승 3안</title><style>'+$css+'</style><main><div class="eyebrow">ASTELLA / PART I · STAGE 05</div><h1>유스티티아 하드 — 노멀 계승 3안</h1><p>가는 체형 · 물결 머리 · 눈가리개 · 긴 예복 · 대검 · 두 접시 저울<br>같은 성상의 정체성을 유지하고, 하중과 균열로 하드 변형을 표현했습니다.</p><nav class="toolbar" aria-label="보기 선택"><button data-view="all" aria-pressed="true">전체 비교</button><button data-view="1" aria-pressed="false">1 · 무너진 균형</button><button data-view="2" aria-pressed="false">2 · 개방된 판결</button><button data-view="3" aria-pressed="false">3 · 봉인 균열</button></nav><section class="cards">'+($cards -join '')+'</section><details><summary>노멀 원본 펼쳐보기</summary><img loading="eager" decoding="sync" src="'+$normalSrc+'" alt="유스티티아 노멀 원본"></details><div class="foot">1536 × 1024 PNG · built-in image_gen · 3안 비교 후보</div></main><script>'+$js+'</script></html>'
}
$srcs=@($rows | ForEach-Object {$_.image})
[IO.File]::WriteAllText((Join-Path $root 'INDEX.html'),(Build-Html $srcs 'Reference/01-normal.png' $false),$utf8)
Add-Type -AssemblyName System.Drawing
function Data-Jpeg($path,$width){
  $im=[Drawing.Image]::FromFile($path)
  $height=[int]([double]$im.Height*$width/$im.Width)
  $bmp=New-Object Drawing.Bitmap($width,$height)
  $g=[Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.DrawImage($im,0,0,$width,$height)
  $codec=[Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object {$_.MimeType -eq 'image/jpeg'}
  $param=New-Object Drawing.Imaging.EncoderParameters(1)
  $param.Param[0]=New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality,([long]68))
  $ms=New-Object IO.MemoryStream
  $bmp.Save($ms,$codec,$param)
  $result='data:image/jpeg;base64,'+[Convert]::ToBase64String($ms.ToArray())
  $param.Dispose();$ms.Dispose();$g.Dispose();$bmp.Dispose();$im.Dispose()
  return $result
}
$inlineSrc=@($rows | ForEach-Object {Data-Jpeg (Join-Path $root $_.image) 720})
$inlineNormal=Data-Jpeg $normal 900
$inlineHtml=Build-Html $inlineSrc $inlineNormal $true
$scratch=$env:ORCA_CHAT_SCRATCH_DIR
$inlineName='justitia-hard-normal-preserved-'+([Guid]::NewGuid().ToString('N').Substring(0,8))+'.html'
if($scratch){
  $inlinePath=Join-Path $scratch $inlineName
  [IO.File]::WriteAllText($inlinePath,$inlineHtml,$utf8)
  if((Get-Item -LiteralPath $inlinePath).Length -ge 524288){throw 'Inline preview too large'}
}
[pscustomobject]@{gallery=(Join-Path $root 'INDEX.html');inline=$inlineName;inline_bytes=[Text.Encoding]::UTF8.GetByteCount($inlineHtml);images=3;png_hashes_verified=$true} | ConvertTo-Json -Compress

