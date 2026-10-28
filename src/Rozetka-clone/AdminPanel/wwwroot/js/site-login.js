window.trebaSiteLogin = {
    async logout() {
        const response = await fetch('/site-login/logout', { method: 'POST', credentials: 'same-origin' });
        if (!response.ok) throw new Error('Could not end the admin session.');
    },
    returnToStorefront(storeOrigin) {
        const destination = new URL('/shop-api/staff-exit', storeOrigin);
        const form = document.createElement('form');
        form.method = 'POST';
        form.action = destination.href;
        document.body.appendChild(form);
        form.submit();
    }
};
