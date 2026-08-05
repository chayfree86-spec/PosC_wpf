using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.App.Helpers;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

namespace Pos.App.ViewModels;

public partial class LedgerViewModel : ObservableObject
{
    private readonly CustomerLedgerRepository _ledgerRepo;
    private readonly SyncCoordinator _sync;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private double _totalGaveBalance;   // total receivable (udhaar)
    [ObservableProperty] private double _totalGotBalance;    // total prepaid / advance
    [ObservableProperty] private double _selectedDebited;    // this customer: total borrowed
    [ObservableProperty] private double _selectedCredited;   // this customer: total paid

    public ObservableCollection<Customer> Customers { get; } = new();
    public FastObservableCollection<Customer> FilteredCustomers { get; } = new();
    public ObservableCollection<LedgerEntry> CustomerEntries { get; } = new();

    public int CustomerCount => Customers.Count;

    public LedgerViewModel(CustomerLedgerRepository ledgerRepo, SyncCoordinator sync)
    {
        _ledgerRepo = ledgerRepo;
        _sync = sync;
        LoadData();
    }

    public void LoadData()
    {
        // Pull the server's ledger into SQLite first, so the list is every customer on the server
        // and not just the ones this till created. Best-effort: offline, it reads the local copy.
        _sync.RefreshLedgerNow();

        var keepId = SelectedCustomer?.Id;
        Customers.Clear();
        FilteredCustomers.Clear();

        double totalGave = 0, totalGot = 0;
        foreach (var c in _ledgerRepo.GetCustomers())
        {
            Customers.Add(c);
            if (c.Balance > 0) totalGave += c.Balance;
            else if (c.Balance < 0) totalGot += Math.Abs(c.Balance);
        }
        ApplySearch();

        TotalGaveBalance = totalGave;
        TotalGotBalance = totalGot;
        OnPropertyChanged(nameof(CustomerCount));

        SelectedCustomer = (keepId != null ? Customers.FirstOrDefault(c => c.Id == keepId) : null)
                           ?? FilteredCustomers.FirstOrDefault();
    }

    private void ApplySearch()
    {
        var q = (SearchText ?? "").Trim().ToLowerInvariant();
        var list = new List<Customer>();
        foreach (var c in Customers)
        {
            if (q.Length == 0 || c.Name.ToLowerInvariant().Contains(q) || c.Phone.Contains(q))
            {
                list.Add(c);
            }
        }
        FilteredCustomers.ReplaceAll(list);
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        CustomerEntries.Clear();
        double debited = 0, credited = 0;
        if (value != null)
        {
            foreach (var e in _ledgerRepo.GetLedgerEntries(value.Id))
            {
                CustomerEntries.Add(e);
                var t = (e.Type ?? "").ToLowerInvariant();
                if (t is "gave" or "debit") debited += e.Amount;
                else credited += e.Amount;   // got / credit / payment
            }
        }
        SelectedDebited = debited;
        SelectedCredited = credited;
    }

    public void AddCustomer(string name, string phone, string address, double openingBalance)
    {
        var cust = new Customer
        {
            ClientId = _ledgerRepo.ClientId, Name = name, Phone = phone, Address = address, Balance = openingBalance,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        var id = _ledgerRepo.SaveCustomer(cust);
        // Opening balance as an initial ledger entry so the computed balance reflects it.
        if (openingBalance != 0)
        {
            _ledgerRepo.AddLedgerEntry(new LedgerEntry
            {
                ClientId = _ledgerRepo.ClientId, CustomerId = id,
                Type = openingBalance > 0 ? "gave" : "got",
                Amount = Math.Abs(openingBalance), PaymentMode = "cash", Remarks = "Opening balance",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        SelectedCustomer = null;
        LoadData();
        SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
    }

    public void EditCustomer(Customer c, string name, string phone, string address)
    {
        c.Name = name; c.Phone = phone; c.Address = address;
        _ledgerRepo.SaveCustomer(c);
        LoadData();
    }

    public void DeleteSelectedCustomer()
    {
        if (SelectedCustomer == null) return;
        _ledgerRepo.DeleteCustomer(SelectedCustomer.Id);
        SelectedCustomer = null;
        LoadData();
    }

    public void AddEntry(string type, double amount, string mode, string remarks, DateTime date)
    {
        if (SelectedCustomer == null) return;
        _ledgerRepo.AddLedgerEntry(new LedgerEntry
        {
            ClientId = _ledgerRepo.ClientId, CustomerId = SelectedCustomer.Id, Type = type, Amount = amount,
            PaymentMode = mode, Remarks = remarks, CreatedAt = date.ToString("yyyy-MM-dd HH:mm:ss")
        });
        LoadData();
    }

    public void UpdateEntry(LedgerEntry entry, string type, double amount, string mode, string remarks, DateTime date)
    {
        entry.Type = type; entry.Amount = amount; entry.PaymentMode = mode; entry.Remarks = remarks;
        entry.CreatedAt = date.ToString("yyyy-MM-dd HH:mm:ss");
        _ledgerRepo.UpdateLedgerEntry(entry);
        LoadData();
    }

    public void DeleteEntry(LedgerEntry entry)
    {
        _ledgerRepo.DeleteLedgerEntry(entry.Id);
        LoadData();
    }
}
