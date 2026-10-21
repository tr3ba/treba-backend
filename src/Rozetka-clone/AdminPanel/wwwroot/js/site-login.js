window.trebaSiteLogin = {
    async logout() {
        const response = await fetch('/site-login/logout', { method: 'POST', credentials: 'same-origin' });
        if (!response.ok) throw new Error('Could not end the admin session.');
    }
};
