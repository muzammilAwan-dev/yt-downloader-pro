/**
 * @fileoverview Background Service Worker
 * Handles secure cookie extraction and formats them to the Netscape standard
 * required by yt-dlp for age-restriction bypass functionality.
 * @version 5.3.0
 */

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "get_cookies") {
        
        chrome.cookies.getAll({ domain: '.youtube.com' }, (cookies) => {
            if (!cookies || cookies.length === 0) {
                sendResponse("");
                return;
            }

            // Construct Netscape HTTP Cookie File header
            let netscapeFormat = "# Netscape HTTP Cookie File\n";
            netscapeFormat += "# http://curl.haxx.se/rfc/cookie_spec.html\n";
            netscapeFormat += "# This is a generated file!  Do not edit.\n\n";

            // Map Chrome cookie objects to Netscape tab-separated standard
            cookies.forEach(c => {
                const domain = c.domain;
                const includeSubdomains = domain.startsWith('.') ? 'TRUE' : 'FALSE';
                const path = c.path;
                const secure = c.secure ? 'TRUE' : 'FALSE';
                const expiry = c.expirationDate ? Math.floor(c.expirationDate) : Math.floor(Date.now() / 1000) + (3600 * 24 * 30);
                
                netscapeFormat += `${domain}\t${includeSubdomains}\t${path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
            });

            // Encode payload to safely pass through custom URI protocol
            const encodedPayload = btoa(unescape(encodeURIComponent(netscapeFormat)));
            sendResponse(encodedPayload);
        });
        
        return true; // Keep message channel open for asynchronous response
    }
});