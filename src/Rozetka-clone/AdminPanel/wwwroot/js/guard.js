window.trebaGuard = {
    deviceName: () => {
        const ua = navigator.userAgent;
        const browser = ua.includes('Edg/') ? 'Edge' : ua.includes('Firefox/') ? 'Firefox' : ua.includes('Chrome/') ? 'Chrome' : ua.includes('Safari/') ? 'Safari' : 'Браузер';
        const platform = /Windows/.test(ua) ? 'Windows' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Macintosh/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : '';
        return `${browser}${platform ? ' · ' + platform : ''}`;
    }
};
