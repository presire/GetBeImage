using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;


namespace GetBeImage;

public class NumericOnlyTextBox : TextBox
{
    private readonly System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("^[0-9]*$");

    public NumericOnlyTextBox()
    {
        // TextChanged イベントを購読
        this.TextChanged += NumericOnlyTextBox_TextChanged;
    }

    private void NumericOnlyTextBox_TextChanged(object sender, EventArgs e)
    {
        // 入力が数値でない場合、直前のテキストに戻す
        if (!IsNumericInput(Text))
        {
            int caretIndex = CaretIndex;
            Text = GetLastValidText();
            CaretIndex = caretIndex > Text.Length ? Text.Length : caretIndex;
        }
    }

    private bool IsNumericInput(string text)
    {
        // 入力が数値かどうかを正規表現でチェック
        return regex.IsMatch(text);
    }

    private string GetLastValidText()
    {
        // 直前のテキストを返す
        var oldValue = (string)GetValue(TextProperty);
        return oldValue ?? string.Empty;
    }
}
