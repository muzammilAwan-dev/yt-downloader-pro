// Generates host/YTDLPHost.Tests/Native/GoldenCases.cs: for each case, the command the CURRENT extension builds (statements copied
// from content.js launchDownload / handleChannelDownload, using the real sites.js helpers) next to the native request that expresses
// the same choices. The C# tests map the request and require the same argument list.
const vm = require('vm'), fs = require('fs');
const sandbox = { self: {} }; vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(process.argv[2] + '/extension/sites.js', 'utf8'), sandbox);
const ytdlpNameTag = sandbox.self.ytdlpNameTag, ytdlpUniqueSuffix = sandbox.self.ytdlpUniqueSuffix;

const ALL = { subtitles: true, playlist: true, metadata: true, thumbnail: true, sponsorBlock: true, compatMode: true };
const sites = {
  yt: { features: ALL },
  twitch: { features: ALL, cropKeyframeCuts: false },
  tiktok: { features: ALL, uniqueFilenames: true },
  bare: { features: { subtitles: false, playlist: false, metadata: false, thumbnail: false, sponsorBlock: false, compatMode: false } },
};
const SAVE_DEFAULT = '~/Downloads/YTDownloaderPro/';

function legacySingle(c) {            // === content.js launchDownload (statements copied) ===
  const SITE = sites[c.site || 'yt'], resolution = c.res, wantsSubs = !!c.subs, wantsPlaylist = !!c.playlist, playlistItems = c.items || '';
  const isCropped = !!c.crop, sTime = c.crop ? c.crop[0] : '', eTime = c.crop ? c.crop[1] : '', videoUrl = c.url;
  const prefs = c.prefs || {};
  let saveDir = SAVE_DEFAULT;
  const speed = prefs.concurrentDownloads || '4';
  const audioFmt = prefs.audioFormat || 'mp3';
  let fmt;
  if (resolution === 'audio') {
    let quality = audioFmt === 'flac' || audioFmt === 'wav' ? '' : '--audio-quality 0';
    fmt = { f: 'ba', opt: `--extract-audio --audio-format ${audioFmt} ${quality}`.trim(), ext: audioFmt };
  } else if (resolution === '1440' || resolution === '2160') {
    fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mkv', ext: 'mkv' };
  } else {
    fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
  }
  const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
  let cropperCmd = '';
  if (isCropped && (sTime || eTime)) {
    const startSec = sTime ? sTime : '00:00:00';
    const endSec = eTime ? eTime : 'inf';
    cropperCmd = `--download-sections "*${startSec}-${endSec}" ${SITE.cropKeyframeCuts === false ? '' : '--force-keyframes-at-cuts '}`;
  }
  const subCmd = (wantsSubs && resolution !== 'audio' && SITE.features.subtitles)
    ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" --sleep-subtitles 5 ' : '';
  let plCmd = '--no-playlist ';
  if (wantsPlaylist && SITE.features.playlist) {
    plCmd = '--yes-playlist ';
    if (playlistItems) { plCmd += `--playlist-items "${playlistItems}" `; }
  }
  const metaFlag = (prefs.flagMetadata === true && SITE.features.metadata) ? '--embed-metadata' : '';
  const thumbFlag = (prefs.flagThumbnail !== false && SITE.features.thumbnail && fmt.ext !== 'wav') ? '--embed-thumbnail' : '';
  const sponsorFlag = (prefs.flagSponsor === true && SITE.features.sponsorBlock) ? '--sponsorblock-remove all' : '';
  const compatFlag = (prefs.compatMode === true && SITE.features.compatMode) ? '-S "vcodec:h264,res,acodec:m4a"' : '';
  const nameTag = ytdlpNameTag({ cropped: !!cropperCmd, cropStart: sTime, cropEnd: eTime, sponsor: !!sponsorFlag, compat: !!compatFlag && resolution !== 'audio' });
  const outTemplate = wantsPlaylist && SITE.features.playlist
    ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s${nameTag}_${resLabel}.${fmt.ext}"`
    : `-o "${saveDir}%(title)s${ytdlpUniqueSuffix(SITE, videoUrl)}${nameTag}_${resLabel}.${fmt.ext}"`;
  const command = ['yt-dlp', '-f', `"${fmt.f}"`, compatFlag, fmt.opt, `-N ${speed}`, cropperCmd, subCmd, plCmd, sponsorFlag, '--restrict-filenames', metaFlag, thumbFlag, '--no-warnings', '--progress', outTemplate, `"${videoUrl}"`].filter(Boolean).join(' ');
  // what the future native client sends: the EFFECTIVE choices after the site gating above
  const options = {
    quality: resolution, audioFormat: audioFmt,
    subs: !!subCmd, compat: !!compatFlag, sponsorBlock: !!sponsorFlag, embedMetadata: !!metaFlag, embedThumbnail: prefs.flagThumbnail !== false && SITE.features.thumbnail,
    playlist: wantsPlaylist && SITE.features.playlist, concurrentFragments: parseInt(speed, 10),
    cropKeyframeCuts: SITE.cropKeyframeCuts !== false, uniqueFilename: !!SITE.uniqueFilenames,
  };
  if (options.playlist && playlistItems) options.playlistItems = playlistItems;
  if (cropperCmd) options.trim = { start: sTime, end: eTime };
  return { command, options, url: videoUrl };
}

function legacyChannel(c) {           // === content.js handleChannelDownload (statements copied) ===
  const SITE = sites[c.site || 'yt'], resolution = c.res, prefs = c.prefs || {};
  const saveDir = SAVE_DEFAULT, speed = prefs.concurrentDownloads || '4', audioFmt = prefs.audioFormat || 'mp3';
  let fmt;
  if (resolution === 'audio') fmt = { f: 'ba', opt: `--extract-audio --audio-format ${audioFmt}`, ext: audioFmt };
  else fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
  const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
  const channelUrl = c.url, itemUrls = c.items || [];
  const slug = c.slug;
  const archiveFile = `${saveDir}${slug}.archive.txt`;
  const metaFlag = (prefs.flagMetadata === true) ? '--embed-metadata' : '';
  const thumbFlag = (prefs.flagThumbnail !== false && fmt.ext !== 'wav') ? '--embed-thumbnail' : '';
  const compatFlag = (prefs.compatMode === true && SITE.features.compatMode) ? '-S "vcodec:h264,res,acodec:m4a"' : '';
  const nameTemplate = SITE.uniqueFilenames ? '%(title)s_%(id)s' : '%(title)s';
  const base = ['yt-dlp', '--yes-playlist', '--ignore-errors', '-f', `"${fmt.f}"`, compatFlag, fmt.opt, `-N ${speed}`,
    '--sleep-requests 1', '--sleep-interval 5', '--max-sleep-interval 15', `--download-archive "${archiveFile}"`,
    '--restrict-filenames', metaFlag, thumbFlag, '--no-warnings', '--progress',
    `-o "${saveDir}%(channel,uploader)s/${nameTemplate}_${resLabel}.${fmt.ext}"`].filter(Boolean).join(' ');
  const command = [base, `"${channelUrl}"`, ...itemUrls.map((u) => `"${u}"`)].join(' ');
  const options = { quality: resolution, audioFormat: audioFmt, compat: !!compatFlag, embedMetadata: !!metaFlag, embedThumbnail: prefs.flagThumbnail !== false,
    concurrentFragments: parseInt(speed, 10), uniqueFilename: !!SITE.uniqueFilenames, job: 'channel', archiveName: slug };
  return { command, options, urls: [channelUrl, ...itemUrls] };
}

const YT = 'https://www.youtube.com/watch?v=abcdefghijk';
const TT = 'https://www.tiktok.com/@someone/video/7123456789012345678';
const single = [
  ['video 1080 defaults', { res: '1080', url: YT }],
  ['video 360', { res: '360', url: YT }],
  ['video 720 other fragments', { res: '720', url: YT, prefs: { concurrentDownloads: '8' } }],
  ['video 1440 is mkv', { res: '1440', url: YT }],
  ['video 2160 is mkv', { res: '2160', url: YT }],
  ['audio mp3', { res: 'audio', url: YT }],
  ['audio m4a', { res: 'audio', url: YT, prefs: { audioFormat: 'm4a' } }],
  ['audio flac has no quality flag', { res: 'audio', url: YT, prefs: { audioFormat: 'flac' } }],
  ['audio wav has no quality flag and no thumbnail', { res: 'audio', url: YT, prefs: { audioFormat: 'wav' } }],
  ['subtitles on a video', { res: '1080', url: YT, subs: true }],
  ['subtitles are ignored for audio', { res: 'audio', url: YT, subs: true }],
  ['crop start and end', { res: '1080', url: YT, crop: ['00:00:00', '01:00'] }],
  ['crop only start', { res: '1080', url: YT, crop: ['1:15', ''] }],
  ['crop only end', { res: '1080', url: YT, crop: ['', '30'] }],
  ['crop without keyframe cuts (twitch)', { res: '720', url: YT, crop: ['10', '20'], site: 'twitch' }],
  ['playlist', { res: '1080', url: YT, playlist: true }],
  ['playlist with items', { res: '1080', url: YT, playlist: true, items: '1-5,8' }],
  ['sponsorblock', { res: '1080', url: YT, prefs: { flagSponsor: true } }],
  ['h264 compat', { res: '1080', url: YT, prefs: { compatMode: true } }],
  ['h264 compat on audio is still passed', { res: 'audio', url: YT, prefs: { compatMode: true } }],
  ['metadata on thumbnail off', { res: '1080', url: YT, prefs: { flagMetadata: true, flagThumbnail: false } }],
  ['everything at once', { res: '1080', url: YT, subs: true, playlist: true, items: '2,4-6', crop: ['5', '15'], prefs: { flagSponsor: true, compatMode: true, flagMetadata: true, concurrentDownloads: '2' } }],
  ['tiktok video id in the name', { res: '1080', url: TT, site: 'tiktok' }],
  ['tiktok without an id in the link', { res: '1080', url: 'https://www.tiktok.com/@someone', site: 'tiktok' }],
  ['site without optional features', { res: '1080', url: YT, subs: true, playlist: true, crop: ['1', '2'], site: 'bare', prefs: { flagSponsor: true, compatMode: true, flagMetadata: true } }],
];
const channel = [
  ['channel video 720', { res: '720', url: 'https://www.youtube.com/@someone/videos', slug: 'www.youtube.com_@someone_videos'.replace(/[^\w.-]+/g, '_') }],
  ['channel audio mp3 with item links', { res: 'audio', url: 'https://www.tiktok.com/@someone', slug: 'tiktok_someone', site: 'tiktok', items: [TT, 'https://www.tiktok.com/@someone/video/7123456789012345679'] }],
  ['channel 2160 stays mp4', { res: '2160', url: 'https://www.youtube.com/@someone/videos', slug: 'yt_someone' }],
  ['channel with metadata, h264, no thumbnail', { res: '1080', url: 'https://www.youtube.com/@someone/videos', slug: 'yt_someone', prefs: { flagMetadata: true, compatMode: true, flagThumbnail: false } }],
];

const cs = (s) => '@"' + s.replace(/"/g, '""') + '"';
let out = `// <auto-generated> by tools/generate-native-golden.js - do not edit by hand. See that file for how to regenerate.
// Each case: what the extension builds TODAY (content.js) and the native request that expresses the same choices.
namespace YTDLPHost.Tests.Native;

internal static class GoldenCases
{
    public static readonly (string Name, string RequestJson, string ExpectedCommand)[] All =
    {
`;
const rows = [];
single.forEach(([name, c], i) => {
  const r = legacySingle(c);
  const req = { v: 1, id: 'g' + (i + 1), type: 'download', urls: [r.url], options: r.options };
  rows.push(`        (${cs(name)}, ${cs(JSON.stringify(req))}, ${cs(r.command)})`);
});
channel.forEach(([name, c], i) => {
  const r = legacyChannel(c);
  const req = { v: 1, id: 'c' + (i + 1), type: 'download', urls: r.urls, options: r.options };
  rows.push(`        (${cs(name)}, ${cs(JSON.stringify(req))}, ${cs(r.command)})`);
});
out += rows.join(',\n') + `\n    };\n}\n`;
fs.writeFileSync(process.argv[3], out);
console.log('cases: ' + rows.length);
