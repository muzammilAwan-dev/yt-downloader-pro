/**
 * @fileoverview Background Service Worker
 * Handles secure cookie extraction and formats them to the Netscape standard.
 * Features Cookie-Dieting to bypass the 2048-character Windows URL limit.
 * @version 5.4.0
 */

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "get_cookies") {
        
        chrome.cookies.getAll({ domain: '.youtube.com' }, (cookies) => {
            if (!cookies || cookies.length === 0) {
                sendResponse("");
                return;
            }

            // The "Cookie Diet"
            // Only extract the exact cookies yt-dlp needs to verify login/age.
            // This prevents the Base64 string from exceeding the Windows URL character limit.
            const essentialKeys = [
                '__Secure-1PSID', 
                '__Secure-3PSID', 
                '__Secure-1PSIDTS', 
                'LOGIN_INFO', 
                'VISITOR_INFO1_LIVE'
            ];
            
            const filteredCookies = cookies.filter(c => essentialKeys.includes(c.name));

            // Construct Netscape HTTP Cookie File header
            let netscapeFormat = "# Netscape HTTP Cookie File\n";
            netscapeFormat += "# http://curl.haxx.se/rfc/cookie_spec.html\n";
            netscapeFormat += "# This is a generated file!  Do not edit.\n\n";

            // Map ONLY the essential Chrome cookies to Netscape standard
            filteredCookies.forEach(c => {
                const domain = c.domain;
                const includeSubdomains = domain.startsWith('.') ? 'TRUE' : 'FALSE';
                const path = c.path;
                const secure = c.secure ? 'TRUE' : 'FALSE';
                const expiry = c.expirationDate ? Math.floor(c.expirationDate) : Math.floor(Date.now() / 1000) + (3600 * 24 * 30);
                
                netscapeFormat += `${domain}\t${includeSubdomains}\t${path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
            });

            // Encode the much smaller payload
            const encodedPayload = btoa(unescape(encodeURIComponent(netscapeFormat)));
            sendResponse(encodedPayload);
        });
        
        return true; 
    }
});