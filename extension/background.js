/**
 * @fileoverview Background Service Worker
 * Handles secure cookie extraction and formats them to the Netscape standard.
 * Features an upgraded Anti-Bot Cookie Diet to bypass YouTube's latest DRM.
 * @version 6.1.0
 */

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "get_cookies") {
        
        chrome.cookies.getAll({ domain: '.youtube.com' }, (cookies) => {
            if (!cookies || cookies.length === 0) {
                sendResponse("");
                return;
            }

            // OPTIMIZATION 1: Expanded the Cookie Diet to include 'SOCS', 'YSC', and 'PREF'
            // YouTube's latest anti-bot DRM requires these to validate the session.
            const essentialKeys = [
                '__Secure-1PSID', 
                '__Secure-3PSID', 
                '__Secure-1PSIDTS', 
                'LOGIN_INFO', 
                'VISITOR_INFO1_LIVE',
                'VISITOR_PRIVACY_METADATA',
                'SOCS',
                'YSC',
                'PREF'
            ];
            
            const filteredCookies = cookies.filter(c => essentialKeys.includes(c.name));

            let netscapeFormat = "# Netscape HTTP Cookie File\n";
            netscapeFormat += "# http://curl.haxx.se/rfc/cookie_spec.html\n";
            netscapeFormat += "# This is a generated file!  Do not edit.\n\n";

            filteredCookies.forEach(c => {
                const domain = c.domain;
                const includeSubdomains = domain.startsWith('.') ? 'TRUE' : 'FALSE';
                const path = c.path;
                const secure = c.secure ? 'TRUE' : 'FALSE';
                const expiry = c.expirationDate ? Math.floor(c.expirationDate) : Math.floor(Date.now() / 1000) + (3600 * 24 * 30);
                
                netscapeFormat += `${domain}\t${includeSubdomains}\t${path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
            });

            // Modern, safe UTF-8 to Base64 encoding
            const encodedPayload = btoa(unescape(encodeURIComponent(netscapeFormat)));
            sendResponse(encodedPayload);
        });
        
        return true; 
    }
});
